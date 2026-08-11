using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Core.Migrations
{
    /// <inheritdoc />
    public partial class BackfillValidationStatusApproved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plan 11 (.claude/plans/11-show-only-validated-items.md) turns validation into a
            // publication gate: list endpoints outside "Mi acordanza" now hide anything that
            // isn't Approved. The previous migration (ValidationRejectionAndContributors) only
            // backfilled ValidationStatus to Approved where a decision (DecidedAt) had already
            // been recorded, so essentially the entire pre-existing catalogue is still Pending.
            // Approve every row that exists as of this migration so the gate ships as a no-op
            // for existing data, rather than hiding the whole catalogue and dumping hundreds of
            // rows into every moderator's review queue. DecidedAt/DecidedByUserId are left null
            // deliberately — nobody actually reviewed these, so nobody should be credited with
            // having decided them.
            migrationBuilder.Sql(
                """
                UPDATE "core"."Routes" SET "ValidationStatus" = 'Approved' WHERE "ValidationStatus" <> 'Approved';
                UPDATE "core"."Areas" SET "ValidationStatus" = 'Approved' WHERE "ValidationStatus" <> 'Approved';
                UPDATE "core"."FirstAscensionists" SET "ValidationStatus" = 'Approved' WHERE "ValidationStatus" <> 'Approved';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data backfill, not a schema change — there's nothing to reverse (and no way to
            // recover which rows were actually Pending/Rejected before this ran).
        }
    }
}
