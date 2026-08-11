using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Core.Migrations
{
    /// <inheritdoc />
    // Hand-written as renames (not the DropTable/CreateTable the scaffolder
    // produced by default) to preserve existing RouteOpeners/RouteRouteOpeners
    // data — see backend/CLAUDE.md's note on AddUniqueEmailAndPasskeys for the
    // other precedent of hand-editing a generated migration in this project.
    public partial class RenameRouteOpenerToFirstAscensionist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "RouteOpeners",
                schema: "core",
                newName: "FirstAscensionists",
                newSchema: "core");

            migrationBuilder.RenameTable(
                name: "RouteRouteOpeners",
                schema: "core",
                newName: "RouteFirstAscensionists",
                newSchema: "core");

            migrationBuilder.RenameColumn(
                name: "OpenerId",
                schema: "core",
                table: "RouteFirstAscensionists",
                newName: "FirstAscensionistId");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "FirstAscensionists",
                name: "IX_RouteOpeners_Name",
                newName: "IX_FirstAscensionists_Name");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "FirstAscensionists",
                name: "IX_RouteOpeners_UserId",
                newName: "IX_FirstAscensionists_UserId");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "RouteFirstAscensionists",
                name: "IX_RouteRouteOpeners_OpenerId",
                newName: "IX_RouteFirstAscensionists_FirstAscensionistId");

            migrationBuilder.Sql(
                """ALTER TABLE core."FirstAscensionists" RENAME CONSTRAINT "PK_RouteOpeners" TO "PK_FirstAscensionists";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."FirstAscensionists" RENAME CONSTRAINT "FK_RouteOpeners_AspNetUsers_UserId" TO "FK_FirstAscensionists_AspNetUsers_UserId";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."RouteFirstAscensionists" RENAME CONSTRAINT "PK_RouteRouteOpeners" TO "PK_RouteFirstAscensionists";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."RouteFirstAscensionists" RENAME CONSTRAINT "FK_RouteRouteOpeners_RouteOpeners_OpenerId" TO "FK_RouteFirstAscensionists_FirstAscensionists_FirstAscensionis~";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."RouteFirstAscensionists" RENAME CONSTRAINT "FK_RouteRouteOpeners_Routes_RouteId" TO "FK_RouteFirstAscensionists_Routes_RouteId";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE core."RouteFirstAscensionists" RENAME CONSTRAINT "FK_RouteFirstAscensionists_Routes_RouteId" TO "FK_RouteRouteOpeners_Routes_RouteId";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."RouteFirstAscensionists" RENAME CONSTRAINT "FK_RouteFirstAscensionists_FirstAscensionists_FirstAscensionis~" TO "FK_RouteRouteOpeners_RouteOpeners_OpenerId";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."RouteFirstAscensionists" RENAME CONSTRAINT "PK_RouteFirstAscensionists" TO "PK_RouteRouteOpeners";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."FirstAscensionists" RENAME CONSTRAINT "FK_FirstAscensionists_AspNetUsers_UserId" TO "FK_RouteOpeners_AspNetUsers_UserId";""");
            migrationBuilder.Sql(
                """ALTER TABLE core."FirstAscensionists" RENAME CONSTRAINT "PK_FirstAscensionists" TO "PK_RouteOpeners";""");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "RouteFirstAscensionists",
                name: "IX_RouteFirstAscensionists_FirstAscensionistId",
                newName: "IX_RouteRouteOpeners_OpenerId");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "FirstAscensionists",
                name: "IX_FirstAscensionists_UserId",
                newName: "IX_RouteOpeners_UserId");

            migrationBuilder.RenameIndex(
                schema: "core",
                table: "FirstAscensionists",
                name: "IX_FirstAscensionists_Name",
                newName: "IX_RouteOpeners_Name");

            migrationBuilder.RenameColumn(
                name: "FirstAscensionistId",
                schema: "core",
                table: "RouteFirstAscensionists",
                newName: "OpenerId");

            migrationBuilder.RenameTable(
                name: "RouteFirstAscensionists",
                schema: "core",
                newName: "RouteRouteOpeners",
                newSchema: "core");

            migrationBuilder.RenameTable(
                name: "FirstAscensionists",
                schema: "core",
                newName: "RouteOpeners",
                newSchema: "core");
        }
    }
}
