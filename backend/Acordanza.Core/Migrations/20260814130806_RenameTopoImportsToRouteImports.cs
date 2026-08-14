using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Core.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Hand-written, and one of the documented exceptions to "don't edit a scaffolded
    /// migration" (see backend/CLAUDE.md). The diff engine scaffolded this as DROP TABLE
    /// "TopoImports" + CREATE TABLE "RouteImports", which is correct about the shape and
    /// wrong about the data: plan 30 shipped "TopoImports" to production, and every row in
    /// it is a real import somebody submitted, with its outcome and its route. Renaming in
    /// place keeps them; dropping the table would silently erase the evidence of how a
    /// handful of routes got created.
    ///
    /// So: rename the table, its columns and every constraint/index name that carried the
    /// old one, then add plan 31's three new columns, relax the Image-only ones to
    /// nullable, and put the check constraint on last — it can only hold once "Kind" is
    /// backfilled.
    /// </remarks>
    public partial class RenameTopoImportsToRouteImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE core."TopoImports" RENAME TO "RouteImports";
                ALTER TABLE core."RouteImports" RENAME COLUMN "UploadedByUserId" TO "SubmittedByUserId";
                ALTER INDEX core."PK_TopoImports" RENAME TO "PK_RouteImports";
                ALTER INDEX core."IX_TopoImports_AreaId" RENAME TO "IX_RouteImports_AreaId";
                ALTER INDEX core."IX_TopoImports_RouteId" RENAME TO "IX_RouteImports_RouteId";
                ALTER INDEX core."IX_TopoImports_UploadedByUserId" RENAME TO "IX_RouteImports_SubmittedByUserId";
                ALTER TABLE core."RouteImports"
                    RENAME CONSTRAINT "FK_TopoImports_Areas_AreaId" TO "FK_RouteImports_Areas_AreaId";
                ALTER TABLE core."RouteImports"
                    RENAME CONSTRAINT "FK_TopoImports_AspNetUsers_UploadedByUserId" TO "FK_RouteImports_AspNetUsers_SubmittedByUserId";
                ALTER TABLE core."RouteImports"
                    RENAME CONSTRAINT "FK_TopoImports_Routes_RouteId" TO "FK_RouteImports_Routes_RouteId";
                """);

            // Nullable, backfilled, then tightened — rather than an AddColumn defaultValue,
            // which would leave a store-side DEFAULT the model doesn't declare and which
            // the next migration would then scaffold a DropDefault for.
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "core",
                table: "RouteImports",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Every row that predates this migration is a topo photo — the Url kind is
            // what this migration is adding.
            migrationBuilder.Sql("""UPDATE core."RouteImports" SET "Kind" = 'Image' WHERE "Kind" IS NULL;""");

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                schema: "core",
                table: "RouteImports",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                schema: "core",
                table: "RouteImports",
                type: "text",
                nullable: true);

            // The three payload columns are Image-only from here on.
            foreach (var column in new[] { "BlobName", "ContentType", "FileName" })
            {
                migrationBuilder.AlterColumn<string>(
                    name: column,
                    schema: "core",
                    table: "RouteImports",
                    type: "text",
                    nullable: true,
                    oldClrType: typeof(string),
                    oldType: "text");
            }

            migrationBuilder.CreateIndex(
                name: "IX_RouteImports_SourceUrl",
                schema: "core",
                table: "RouteImports",
                column: "SourceUrl");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RouteImport_KindPayload",
                schema: "core",
                table: "RouteImports",
                sql: """("Kind" = 'Image' AND "BlobName" IS NOT NULL) OR ("Kind" = 'Url' AND "SourceUrl" IS NOT NULL)""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A Url import has no BlobName, so it cannot survive a table whose BlobName is
            // NOT NULL. Dropping those rows is the only honest reversal, and it's why this
            // Down is a real one rather than a throw: the rows it drops are exactly the
            // ones the feature being reverted created.
            migrationBuilder.DropCheckConstraint(
                name: "CK_RouteImport_KindPayload",
                schema: "core",
                table: "RouteImports");

            migrationBuilder.Sql("""DELETE FROM core."RouteImports" WHERE "Kind" = 'Url';""");

            migrationBuilder.DropIndex(
                name: "IX_RouteImports_SourceUrl",
                schema: "core",
                table: "RouteImports");

            migrationBuilder.DropColumn(name: "SourceUrl", schema: "core", table: "RouteImports");
            migrationBuilder.DropColumn(name: "Kind", schema: "core", table: "RouteImports");

            foreach (var column in new[] { "BlobName", "ContentType", "FileName" })
            {
                migrationBuilder.AlterColumn<string>(
                    name: column,
                    schema: "core",
                    table: "RouteImports",
                    type: "text",
                    nullable: false,
                    defaultValue: "",
                    oldClrType: typeof(string),
                    oldType: "text",
                    oldNullable: true);
            }

            migrationBuilder.Sql("""
                ALTER TABLE core."RouteImports"
                    RENAME CONSTRAINT "FK_RouteImports_Routes_RouteId" TO "FK_TopoImports_Routes_RouteId";
                ALTER TABLE core."RouteImports"
                    RENAME CONSTRAINT "FK_RouteImports_AspNetUsers_SubmittedByUserId" TO "FK_TopoImports_AspNetUsers_UploadedByUserId";
                ALTER TABLE core."RouteImports"
                    RENAME CONSTRAINT "FK_RouteImports_Areas_AreaId" TO "FK_TopoImports_Areas_AreaId";
                ALTER INDEX core."IX_RouteImports_SubmittedByUserId" RENAME TO "IX_TopoImports_UploadedByUserId";
                ALTER INDEX core."IX_RouteImports_RouteId" RENAME TO "IX_TopoImports_RouteId";
                ALTER INDEX core."IX_RouteImports_AreaId" RENAME TO "IX_TopoImports_AreaId";
                ALTER INDEX core."PK_RouteImports" RENAME TO "PK_TopoImports";
                ALTER TABLE core."RouteImports" RENAME COLUMN "SubmittedByUserId" TO "UploadedByUserId";
                ALTER TABLE core."RouteImports" RENAME TO "TopoImports";
                """);
        }
    }
}
