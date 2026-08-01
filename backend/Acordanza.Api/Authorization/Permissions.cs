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

    public const string TickCreate = "tick.create";
    public const string TickEdit = "tick.edit";
    public const string TickValidate = "tick.validate";
    public const string TickDelete = "tick.delete";

    public const string FirstAscensionistCreate = "first-ascensionist.create";
    public const string FirstAscensionistEdit = "first-ascensionist.edit";
    public const string FirstAscensionistValidate = "first-ascensionist.validate";
    public const string FirstAscensionistDelete = "first-ascensionist.delete";

    public const string UserManage = "user.manage";

    public const string AreaExpertRequest = "area-expert.request";
    public const string AreaExpertApprove = "area-expert.approve";

    public const string FirstAscensionistLinkRequest = "first-ascensionist-link.request";
    public const string FirstAscensionistLinkApprove = "first-ascensionist-link.approve";

    public static readonly IReadOnlyList<string> All =
    [
        AreaCreate, AreaEdit, AreaValidate, AreaDelete,
        RouteCreate, RouteEdit, RouteValidate, RouteDelete,
        TickCreate, TickEdit, TickValidate, TickDelete,
        FirstAscensionistCreate, FirstAscensionistEdit, FirstAscensionistValidate, FirstAscensionistDelete,
        UserManage,
        AreaExpertRequest, AreaExpertApprove,
        FirstAscensionistLinkRequest, FirstAscensionistLinkApprove
    ];
}
