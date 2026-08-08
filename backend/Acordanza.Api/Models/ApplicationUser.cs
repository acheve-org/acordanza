using Microsoft.AspNetCore.Identity;

namespace Acordanza.Api.Models;

public class ApplicationUser : IdentityUser
{
    public string Name { get; set; } = "";

    public string? InvitedByUserId { get; set; }
    public ApplicationUser? InvitedBy { get; set; }
    public DateTimeOffset? InvitedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}
