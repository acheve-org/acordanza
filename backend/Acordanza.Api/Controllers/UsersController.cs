using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Controllers;

[Authorize(Policy = Permissions.UserManage)]
[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext db) : ControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;
    private static readonly HashSet<string> SortableFields = ["name", "email", "role"];

    [HttpGet]
    public async Task<ActionResult<PagedUsersResponse>> GetUsers(
        [FromQuery] string? q, [FromQuery] string? role,
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string sortBy = "name", [FromQuery] string sortDir = "asc")
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;
        if (!SortableFields.Contains(sortBy)) sortBy = "name";

        // A plain join on UserRoles would emit one row per user-role pair, which
        // would double-count (and mis-paginate) any account ever assigned more
        // than one role. RoleSeeder/create-user only ever assign one, but nothing
        // enforces that invariant at the data level, so this picks a single,
        // deterministic role per user via a correlated subquery instead.
        var query =
            from user in db.Users.AsNoTracking()
            select new
            {
                user.Id,
                user.Name,
                Email = user.Email!,
                Role = (from userRole in db.UserRoles
                        join r in db.Roles on userRole.RoleId equals r.Id
                        where userRole.UserId == user.Id
                        orderby r.Name
                        select r.Name).FirstOrDefault() ?? Roles.User
            };

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(u => u.Name.Contains(q) || u.Email.Contains(q) || u.Role.Contains(q));
        }
        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => u.Role == role);
        }

        var totalCount = await query.CountAsync();

        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy, descending) switch
        {
            ("email", true) => query.OrderByDescending(u => u.Email),
            ("email", false) => query.OrderBy(u => u.Email),
            ("role", true) => query.OrderByDescending(u => u.Role),
            ("role", false) => query.OrderBy(u => u.Role),
            (_, true) => query.OrderByDescending(u => u.Name),
            _ => query.OrderBy(u => u.Name)
        };

        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var dtos = items.Select(u => new UserAccountDto(u.Id, u.Name, u.Email, u.Role)).ToList();

        return Ok(new PagedUsersResponse(dtos, totalCount, page, pageSize));
    }
}
