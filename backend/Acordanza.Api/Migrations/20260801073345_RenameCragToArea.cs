using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Api.Migrations
{
    /// <inheritdoc />
    // Hand-written as renames (not the DropTable/CreateTable the scaffolder
    // produced by default) to preserve existing Crags/Routes data — see
    // backend/CLAUDE.md's note on AddUniqueEmailAndPasskeys, and
    // RenameRouteOpenerToFirstAscensionist, for the same precedent.
    public partial class RenameCragToArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Crags",
                schema: "core",
                newName: "Areas",
                newSchema: "core");

            migrationBuilder.RenameColumn(
                name: "ParentCragId",
                schema: "core",
                table: "Areas",
                newName: "ParentAreaId");

            migrationBuilder.RenameColumn(
                name: "CragId",
                schema: "core",
                table: "Routes",
                newName: "AreaId");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "Areas",
                name: "IX_Crags_Name",
                newName: "IX_Areas_Name");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "Areas",
                name: "IX_Crags_ParentCragId",
                newName: "IX_Areas_ParentAreaId");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "Routes",
                name: "IX_Routes_CragId",
                newName: "IX_Routes_AreaId");

            migrationBuilder.Sql(
                """ALTER TABLE core."Areas" RENAME CONSTRAINT "PK_Crags" TO "PK_Areas";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."Areas" RENAME CONSTRAINT "FK_Crags_Crags_ParentCragId" TO "FK_Areas_Areas_ParentAreaId";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."Routes" RENAME CONSTRAINT "FK_Routes_Crags_CragId" TO "FK_Routes_Areas_AreaId";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE core."Routes" RENAME CONSTRAINT "FK_Routes_Areas_AreaId" TO "FK_Routes_Crags_CragId";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."Areas" RENAME CONSTRAINT "FK_Areas_Areas_ParentAreaId" TO "FK_Crags_Crags_ParentCragId";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."Areas" RENAME CONSTRAINT "PK_Areas" TO "PK_Crags";""");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "Routes",
                name: "IX_Routes_AreaId",
                newName: "IX_Routes_CragId");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "Areas",
                name: "IX_Areas_ParentAreaId",
                newName: "IX_Crags_ParentCragId");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "Areas",
                name: "IX_Areas_Name",
                newName: "IX_Crags_Name");

            migrationBuilder.RenameColumn(
                name: "AreaId",
                schema: "core",
                table: "Routes",
                newName: "CragId");

            migrationBuilder.RenameColumn(
                name: "ParentAreaId",
                schema: "core",
                table: "Areas",
                newName: "ParentCragId");

            migrationBuilder.RenameTable(
                name: "Areas",
                schema: "core",
                newName: "Crags",
                newSchema: "core");
        }
    }
}
