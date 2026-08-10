namespace Acordanza.Api.Dtos;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record UpdateProfileRequest(string Name);

public record ChangeEmailRequest(string NewEmail, string CurrentPassword);
