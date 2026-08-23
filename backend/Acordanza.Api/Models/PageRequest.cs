namespace Acordanza.Api.Models;

// The `?page=&pageSize=` clamp shared by every paginated list endpoint (areas,
// contributions, first ascensionists, ticks, users) — previously five near-identical
// copies of the same two `if` checks, one per controller. Kept as a plain helper
// rather than a [FromQuery]-bound type: binding a complex object would rename the
// query parameters in the OpenAPI document (their C# property names, "Page"/"PageSize",
// aren't the same casing as the "page"/"pageSize" query keys today) and drop their
// documented defaults, which is exactly the kind of accidental contract change a pure
// refactor must not make. Each controller keeps its own `page`/`pageSize` action
// parameters (and its own default/max, which aren't all identical — TicksController
// defaults to 20, everyone else to 10) and just calls Clamp once instead of repeating
// the two `if`s.
public readonly record struct PageRequest(int Page, int PageSize)
{
    public static PageRequest Clamp(int page, int pageSize, int defaultPageSize, int maxPageSize = 100) =>
        new(
            page < 1 ? 1 : page,
            pageSize < 1 || pageSize > maxPageSize ? defaultPageSize : pageSize);
}
