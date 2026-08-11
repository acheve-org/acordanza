namespace Acordanza.Api.Authorization;

public static class Permissions
{
    public const string AreaCreate = "area.create";
    public const string AreaEdit = "area.edit";
    public const string AreaValidate = "area.validate";
    public const string AreaDelete = "area.delete";

    public const string RouteCreate = "route.create";
    public const string RouteEdit = "route.edit";
    public const string RouteValidate = "route.validate";
    public const string RouteDelete = "route.delete";
    // Admin-only by construction: added to All (below) but not to DynamicPermissions'
    // FirstAscensionistLinkGrant nor to RolePermissions' user/moderator sets, so it lands
    // only in AdminPermissions ([.. Permissions.All]). Deliberately not folded into
    // RouteEdit — spending money on a web search is a different question from editing a
    // route, and RouteEdit is also granted dynamically to every linked first ascensionist,
    // which would hand the spend button to a set of people that grows on its own.
    public const string RouteEnrich = "route.enrich";

    public const string TickCreate = "tick.create";
    public const string TickEdit = "tick.edit";
    public const string TickValidate = "tick.validate";
    public const string TickDelete = "tick.delete";

    public const string FirstAscensionistCreate = "first-ascensionist.create";
    public const string FirstAscensionistEdit = "first-ascensionist.edit";
    public const string FirstAscensionistValidate = "first-ascensionist.validate";
    public const string FirstAscensionistDelete = "first-ascensionist.delete";

    public const string UserManage = "user.manage";
    public const string UserInvite = "user.invite";

    public const string AreaExpertRequest = "area-expert.request";
    public const string AreaExpertApprove = "area-expert.approve";

    public const string FirstAscensionistLinkRequest = "first-ascensionist-link.request";
    public const string FirstAscensionistLinkApprove = "first-ascensionist-link.approve";

    public static readonly IReadOnlyList<string> All =
    [
        AreaCreate, AreaEdit, AreaValidate, AreaDelete,
        RouteCreate, RouteEdit, RouteValidate, RouteDelete, RouteEnrich,
        TickCreate, TickEdit, TickValidate, TickDelete,
        FirstAscensionistCreate, FirstAscensionistEdit, FirstAscensionistValidate, FirstAscensionistDelete,
        UserManage, UserInvite,
        AreaExpertRequest, AreaExpertApprove,
        FirstAscensionistLinkRequest, FirstAscensionistLinkApprove
    ];
}
