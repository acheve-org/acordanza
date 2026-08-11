namespace Acordanza.Core.Models;

public class PasskeyCredential
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public required byte[] CredentialId { get; set; }
    public required byte[] PublicKey { get; set; }
    public required uint SignCount { get; set; }
    public required string Name { get; set; }
    public required Guid AaGuid { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
