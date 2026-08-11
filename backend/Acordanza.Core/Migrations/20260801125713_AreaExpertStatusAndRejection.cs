using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Core.Migrations
{
    /// <inheritdoc />
    public partial class AreaExpertStatusAndRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AreaExperts_AspNetUsers_ApprovedByUserId",
                schema: "core",
                table: "AreaExperts");

            migrationBuilder.DropIndex(
                name: "IX_AreaExperts_ApprovedByUserId",
                schema: "core",
                table: "AreaExperts");

            migrationBuilder.DropIndex(
                name: "IX_AreaExperts_AreaId_UserId",
                schema: "core",
                table: "AreaExperts");

            migrationBuilder.RenameColumn(
                name: "ApprovedAt",
                schema: "core",
                table: "AreaExperts",
                newName: "DecidedAt");

            migrationBuilder.RenameColumn(
                name: "ApprovedByUserId",
                schema: "core",
                table: "AreaExperts",
                newName: "DecidedByUserId");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "core",
                table: "AreaExperts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "core",
                table: "AreaExperts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            // Existing rows predate the Rejected status (a rejection used to
            // delete the row outright), so every pre-existing row is either
            // already-approved (DecidedAt set) or still pending.
            migrationBuilder.Sql("""
                UPDATE "core"."AreaExperts" SET "Status" = 'Approved' WHERE "DecidedAt" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AreaExperts_AreaId_UserId",
                schema: "core",
                table: "AreaExperts",
                columns: new[] { "AreaId", "UserId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AreaExperts_DecidedByUserId",
                schema: "core",
                table: "AreaExperts",
                column: "DecidedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AreaExperts_AspNetUsers_DecidedByUserId",
                schema: "core",
                table: "AreaExperts",
                column: "DecidedByUserId",
                principalSchema: "identity",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AreaExperts_AspNetUsers_DecidedByUserId",
                schema: "core",
                table: "AreaExperts");

            migrationBuilder.DropIndex(
                name: "IX_AreaExperts_AreaId_UserId",
                schema: "core",
                table: "AreaExperts");

            migrationBuilder.DropIndex(
                name: "IX_AreaExperts_DecidedByUserId",
                schema: "core",
                table: "AreaExperts");

            // The old schema's unconditional unique (AreaId, UserId) index assumed at
            // most one row per pair — true under the old "reject = delete the row"
            // model. Once rejections started being kept as history (this migration's
            // Up), a user can accumulate multiple rejected rows plus one active
            // (pending or approved) row for the same pair. Collapse each pair back
            // down to a single row — preferring an approved row, else the most
            // recently requested one — before the CREATE UNIQUE INDEX below, or it
            // fails against any duplicates accumulated while this migration was live.
            migrationBuilder.Sql("""
                DELETE FROM "core"."AreaExperts"
                WHERE "Id" IN (
                    SELECT "Id" FROM (
                        SELECT "Id",
                               ROW_NUMBER() OVER (
                                   PARTITION BY "AreaId", "UserId"
                                   ORDER BY CASE WHEN "Status" = 'Approved' THEN 0 ELSE 1 END, "RequestedAt" DESC
                               ) AS rn
                        FROM "core"."AreaExperts"
                    ) ranked
                    WHERE rn > 1
                );
                """);

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "core",
                table: "AreaExperts");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "core",
                table: "AreaExperts");

            migrationBuilder.RenameColumn(
                name: "DecidedByUserId",
                schema: "core",
                table: "AreaExperts",
                newName: "ApprovedByUserId");

            migrationBuilder.RenameColumn(
                name: "DecidedAt",
                schema: "core",
                table: "AreaExperts",
                newName: "ApprovedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AreaExperts_ApprovedByUserId",
                schema: "core",
                table: "AreaExperts",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AreaExperts_AreaId_UserId",
                schema: "core",
                table: "AreaExperts",
                columns: new[] { "AreaId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AreaExperts_AspNetUsers_ApprovedByUserId",
                schema: "core",
                table: "AreaExperts",
                column: "ApprovedByUserId",
                principalSchema: "identity",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
