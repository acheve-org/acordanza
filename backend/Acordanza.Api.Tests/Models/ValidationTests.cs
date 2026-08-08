using Acordanza.Api.Models;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Models;

public class ValidationTests
{
    [Fact]
    public void MarkValidated_approves_and_clears_any_rejection_reason()
    {
        var area = new Area { Name = "Peña Rúa", ValidationStatus = ReviewStatus.Rejected, RejectionReason = "motivo previo" };

        Validation.MarkValidated(area, "user-1");

        area.ValidationStatus.ShouldBe(ReviewStatus.Approved);
        area.DecidedAt.ShouldNotBeNull();
        area.DecidedByUserId.ShouldBe("user-1");
        area.RejectionReason.ShouldBeNull();
    }

    [Fact]
    public void MarkRejected_sets_status_decision_and_reason()
    {
        var area = new Area { Name = "Peña Rúa" };

        Validation.MarkRejected(area, "user-1", "falta información");

        area.ValidationStatus.ShouldBe(ReviewStatus.Rejected);
        area.DecidedAt.ShouldNotBeNull();
        area.DecidedByUserId.ShouldBe("user-1");
        area.RejectionReason.ShouldBe("falta información");
    }

    [Fact]
    public void InvalidateIfChanged_is_a_noop_when_nothing_changed()
    {
        var decidedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var area = new Area
        {
            Name = "Peña Rúa",
            ValidationStatus = ReviewStatus.Approved,
            DecidedAt = decidedAt,
            DecidedByUserId = "user-1",
        };

        Validation.InvalidateIfChanged(area, changed: false);

        area.ValidationStatus.ShouldBe(ReviewStatus.Approved);
        area.DecidedAt.ShouldBe(decidedAt);
        area.DecidedByUserId.ShouldBe("user-1");
    }

    [Fact]
    public void InvalidateIfChanged_resets_to_pending_and_clears_decision_when_changed()
    {
        var area = new Area
        {
            Name = "Peña Rúa",
            ValidationStatus = ReviewStatus.Approved,
            DecidedAt = DateTimeOffset.UtcNow,
            DecidedByUserId = "user-1",
        };

        Validation.InvalidateIfChanged(area, changed: true);

        area.ValidationStatus.ShouldBe(ReviewStatus.Pending);
        area.DecidedAt.ShouldBeNull();
        area.DecidedByUserId.ShouldBeNull();
        area.RejectionReason.ShouldBeNull();
    }
}
