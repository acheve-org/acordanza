using System.Text;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Acordanza.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/passkeys")]
public class PasskeysController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IFido2 fido2,
    IMemoryCache cache) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PasskeySummary>>> List()
    {
        var userId = userManager.GetUserId(User)!;
        var passkeys = await db.PasskeyCredentials
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PasskeySummary(p.Id, p.Name, p.CreatedAt))
            .ToListAsync();
        return Ok(passkeys);
    }

    [HttpPost("register/options")]
    public async Task<ActionResult<RegisterPasskeyOptionsResponse>> RegisterOptions(RegisterPasskeyOptionsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "El nombre es obligatorio" });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var existing = await db.PasskeyCredentials.Where(p => p.UserId == user.Id).ToListAsync();

        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                Id = Encoding.UTF8.GetBytes(user.Id),
                Name = user.Email!,
                DisplayName = user.Email!
            },
            ExcludeCredentials = existing.Select(p => new PublicKeyCredentialDescriptor(p.CredentialId)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Preferred,
                UserVerification = UserVerificationRequirement.Preferred
            },
            AttestationPreference = AttestationConveyancePreference.None
        });

        var sessionId = Guid.NewGuid().ToString();
        cache.Set(RegisterCacheKey(sessionId), new RegisterPasskeyChallenge(user.Id, request.Name.Trim(), options), TimeSpan.FromMinutes(5));

        return Ok(new RegisterPasskeyOptionsResponse(sessionId, options));
    }

    [HttpPost("register/complete")]
    public async Task<ActionResult<PasskeySummary>> RegisterComplete(RegisterPasskeyCompleteRequest request)
    {
        if (!cache.TryGetValue(RegisterCacheKey(request.SessionId), out RegisterPasskeyChallenge? challenge) || challenge is null)
        {
            return BadRequest(new { error = "La solicitud ha caducado, inténtalo de nuevo" });
        }
        cache.Remove(RegisterCacheKey(request.SessionId));

        var userId = userManager.GetUserId(User)!;
        if (challenge.UserId != userId) return Unauthorized();

        try
        {
            var result = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = request.AttestationResponse,
                OriginalOptions = challenge.Options,
                IsCredentialIdUniqueToUserCallback = async (p, _) =>
                    !await db.PasskeyCredentials.AnyAsync(c => c.CredentialId == p.CredentialId)
            });

            var passkey = new PasskeyCredential
            {
                UserId = userId,
                CredentialId = result.Id,
                PublicKey = result.PublicKey,
                SignCount = result.SignCount,
                Name = challenge.Name,
                AaGuid = result.AaGuid
            };
            db.PasskeyCredentials.Add(passkey);
            await db.SaveChangesAsync();

            return Ok(new PasskeySummary(passkey.Id, passkey.Name, passkey.CreatedAt));
        }
        catch (Fido2VerificationException)
        {
            return BadRequest(new { error = "No se pudo registrar la llave de acceso" });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = userManager.GetUserId(User)!;
        var passkey = await db.PasskeyCredentials.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (passkey is not null)
        {
            db.PasskeyCredentials.Remove(passkey);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    private static string RegisterCacheKey(string sessionId) => $"passkey-register:{sessionId}";

    private record RegisterPasskeyChallenge(string UserId, string Name, CredentialCreateOptions Options);
}
