namespace Acordanza.Api.Authorization;

public static class Permissions
{
    public const string CragCreate = "crag.create";
    public const string CragEdit = "crag.edit";
    public const string CragValidate = "crag.validate";
    public const string CragDelete = "crag.delete";

    public const string RouteCreate = "route.create";
    public const string RouteEdit = "route.edit";
    public const string RouteValidate = "route.validate";
    public const string RouteDelete = "route.delete";

    public const string TickCreate = "tick.create";
    public const string TickEdit = "tick.edit";
    public const string TickValidate = "tick.validate";
    public const string TickDelete = "tick.delete";

    public const string OpenerCreate = "opener.create";
    public const string OpenerEdit = "opener.edit";
    public const string OpenerValidate = "opener.validate";
    public const string OpenerDelete = "opener.delete";

    public const string UserManage = "user.manage";

    public static readonly IReadOnlyList<string> All =
    [
        CragCreate, CragEdit, CragValidate, CragDelete,
        RouteCreate, RouteEdit, RouteValidate, RouteDelete,
        TickCreate, TickEdit, TickValidate, TickDelete,
        OpenerCreate, OpenerEdit, OpenerValidate, OpenerDelete,
        UserManage
    ];
}
