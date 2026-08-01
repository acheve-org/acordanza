namespace Acordanza.Api.Authorization;

// Each role's permission set is defined by extending the role below it in the
// hierarchy (user < first-ascensionist < moderator < admin), so bumping
// someone up a role never takes permissions away.
public static class RolePermissions
{
    private static readonly string[] UserPermissions =
    [
        Permissions.AreaCreate,
        Permissions.RouteCreate,
        Permissions.TickCreate,
        Permissions.TickDelete,
        Permissions.FirstAscensionistCreate,
        Permissions.AreaExpertRequest
    ];

    private static readonly string[] FirstAscensionistPermissions =
    [
        .. UserPermissions,
        Permissions.RouteEdit,
        Permissions.RouteValidate,
        Permissions.FirstAscensionistEdit
    ];

    private static readonly string[] ModeratorPermissions =
    [
        .. FirstAscensionistPermissions,
        Permissions.AreaEdit,
        Permissions.AreaValidate,
        Permissions.AreaDelete,
        Permissions.RouteDelete,
        Permissions.TickEdit,
        Permissions.TickValidate,
        Permissions.FirstAscensionistValidate,
        Permissions.FirstAscensionistDelete,
        Permissions.AreaExpertApprove
    ];

    private static readonly string[] AdminPermissions = [.. Permissions.All];

    public static readonly IReadOnlyDictionary<string, string[]> Map = new Dictionary<string, string[]>
    {
        [Roles.User] = UserPermissions,
        [Roles.FirstAscensionist] = FirstAscensionistPermissions,
        [Roles.Moderator] = ModeratorPermissions,
        [Roles.Admin] = AdminPermissions
    };

    public static IReadOnlyCollection<string> PermissionsFor(IEnumerable<string> roles) =>
        roles
            .SelectMany(role => Map.TryGetValue(role, out var permissions) ? permissions : [])
            .Distinct()
            .ToArray();
}
