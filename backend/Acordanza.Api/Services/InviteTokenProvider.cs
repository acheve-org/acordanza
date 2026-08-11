using Acordanza.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Acordanza.Api.Services;

// A DataProtectorTokenProvider dedicated to invite links, so its TokenLifespan
// (7 days — an invite is asynchronous, unlike a self-triggered password
// reset) can be configured independently of the default provider's 1-hour
// lifespan (Program.cs), which the forgot-password flow uses. Needs its own
// concrete type + options type: Identity resolves a named provider by
// concrete DI type, so registering DataProtectorTokenProvider<ApplicationUser>
// itself under a second name would just resolve the same singleton the
// default provider already uses, with the same 1-hour lifespan.
public class InviteTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InviteTokenProviderOptions()
    {
        Name = "InviteTokenProvider";
        TokenLifespan = TimeSpan.FromDays(7);
    }
}

public class InviteTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<InviteTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger);

public static class InviteTokens
{
    public const string ProviderName = "Invite";
    public const string Purpose = "InviteUser";
    public static readonly TimeSpan Lifespan = TimeSpan.FromDays(7);
}
