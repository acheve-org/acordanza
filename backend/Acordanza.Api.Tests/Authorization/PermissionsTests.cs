using System.Reflection;
using Acordanza.Api.Authorization;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Authorization;

public class PermissionsTests
{
    // Permissions.All is hand-maintained; a constant missing from it never gets a
    // policy registered in Program.cs, which fails *open* on that endpoint's
    // [Authorize(Policy = ...)]. Reflection catches drift without reading the table.
    [Fact]
    public void All_lists_every_public_const_string_on_the_class()
    {
        var declaredConstants = typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Permissions.All.ShouldBe(declaredConstants, ignoreOrder: true);
    }
}
