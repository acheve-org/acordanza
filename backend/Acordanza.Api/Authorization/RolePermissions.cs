namespace Acordanza.Api.Authorization;

// Each role's permission set is defined by extending the role below it in the
// hierarchy (user < moderator < admin), so bumping someone up a role never
// takes permissions away. There used to be a "first-ascensionist" rung
// between user and moderator; it's gone now — route.edit/route.validate/
// first-ascensionist.edit are granted dynamically instead, to any user
// linked to a FirstAscensionist record via an approved
// FirstAscensionistLinkRequest (see DynamicPermissions), not through a role.
public static class RolePermissions
{
    private static readonly string[] UserPermissions =
    [
        Permissions.AreaCreate,
        Permissions.RouteCreate,
        Permissions.TickCreate,
        Permissions.TickDelete,
        Permissions.FirstAscensionistCreate,
        Permissions.AreaExpertRequest,
        Permissions.FirstAscensionistLinkRequest,
        Permissions.UserInvite
    ];

    private static readonly string[] ModeratorPermissions =
    [
        .. UserPermissions,
        Permissions.RouteEdit,
        Permissions.RouteValidate,
        Permissions.FirstAscensionistEdit,
        Permissions.AreaEdit,
        Permissions.AreaValidate,
        Permissions.AreaDelete,
        Permissions.RouteDelete,
        Permissions.TickEdit,
        Permissions.TickValidate,
        Permissions.FirstAscensionistValidate,
        Permissions.FirstAscensionistDelete,
        Permissions.AreaExpertApprove,
        Permissions.FirstAscensionistLinkApprove
    ];

    private static readonly string[] AdminPermissions = [.. Permissions.All];

    public static readonly IReadOnlyDictionary<string, string[]> Map = new Dictionary<string, string[]>
    {
        [Roles.User] = UserPermissions,
        [Roles.Moderator] = ModeratorPermissions,
        [Roles.Admin] = AdminPermissions
    };

    public static IReadOnlyCollection<string> PermissionsFor(IEnumerable<string> roles) =>
        roles
            .SelectMany(role => Map.TryGetValue(role, out var permissions) ? permissions : [])
            .Distinct()
            .ToArray();
}
