namespace Acordanza.Api.Authorization;

public static class Roles
{
    public const string Admin = "admin";
    public const string Moderator = "moderator";
    public const string FirstAscensionist = "first-ascensionist";
    public const string User = "user";

    public static readonly IReadOnlyList<string> All = [Admin, Moderator, FirstAscensionist, User];
}
