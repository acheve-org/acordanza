using Acordanza.Api.Models;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Models;

public class ValidationTests
{
    [Fact]
    public void MarkValidated_sets_both_fields()
    {
        var area = new Area { Name = "Peña Rúa" };

        Validation.MarkValidated(area, "user-1");

        area.ValidatedAt.ShouldNotBeNull();
        area.ValidatedByUserId.ShouldBe("user-1");
    }

    [Fact]
    public void InvalidateIfChanged_is_a_noop_when_nothing_changed()
    {
        var validatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var area = new Area { Name = "Peña Rúa", ValidatedAt = validatedAt, ValidatedByUserId = "user-1" };

        Validation.InvalidateIfChanged(area, changed: false);

        area.ValidatedAt.ShouldBe(validatedAt);
        area.ValidatedByUserId.ShouldBe("user-1");
    }

    [Fact]
    public void InvalidateIfChanged_clears_both_fields_when_changed()
    {
        var area = new Area
        {
            Name = "Peña Rúa",
            ValidatedAt = DateTimeOffset.UtcNow,
            ValidatedByUserId = "user-1",
        };

        Validation.InvalidateIfChanged(area, changed: true);

        area.ValidatedAt.ShouldBeNull();
        area.ValidatedByUserId.ShouldBeNull();
    }
}
