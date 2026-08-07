using Acordanza.Api.Authorization;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Authorization;

public class RolePermissionsTests
{
    [Fact]
    public void Moderator_permissions_are_a_superset_of_user_permissions()
    {
        var userPermissions = RolePermissions.PermissionsFor([Roles.User]);
        var moderatorPermissions = RolePermissions.PermissionsFor([Roles.Moderator]);

        moderatorPermissions.ShouldSatisfyAllConditions(
            () => userPermissions.ShouldAllBe(p => moderatorPermissions.Contains(p)),
            () => moderatorPermissions.Count.ShouldBeGreaterThan(userPermissions.Count));
    }

    [Fact]
    public void Admin_permissions_are_a_superset_of_moderator_permissions()
    {
        var moderatorPermissions = RolePermissions.PermissionsFor([Roles.Moderator]);
        var adminPermissions = RolePermissions.PermissionsFor([Roles.Admin]);

        adminPermissions.ShouldSatisfyAllConditions(
            () => moderatorPermissions.ShouldAllBe(p => adminPermissions.Contains(p)),
            () => adminPermissions.Count.ShouldBeGreaterThan(moderatorPermissions.Count));
    }

    [Fact]
    public void Admin_has_every_permission()
    {
        RolePermissions.PermissionsFor([Roles.Admin]).ShouldBe(Permissions.All, ignoreOrder: true);
    }

    [Fact]
    public void Unknown_role_grants_nothing()
    {
        RolePermissions.PermissionsFor(["not-a-role"]).ShouldBeEmpty();
    }

    [Fact]
    public void Multiple_roles_union_and_dedupe_their_permissions()
    {
        var combined = RolePermissions.PermissionsFor([Roles.User, Roles.Moderator]);

        combined.ShouldBe(RolePermissions.PermissionsFor([Roles.Moderator]), ignoreOrder: true);
        combined.ShouldBeUnique();
    }

    [Fact]
    public void No_roles_grants_nothing()
    {
        RolePermissions.PermissionsFor([]).ShouldBeEmpty();
    }
}
