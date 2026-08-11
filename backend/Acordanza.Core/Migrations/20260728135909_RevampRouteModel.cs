using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Acordanza.Core.Migrations
{
    /// <inheritdoc />
    public partial class RevampRouteModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Crags",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ParentCragId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crags_Crags_ParentCragId",
                        column: x => x.ParentCragId,
                        principalSchema: "core",
                        principalTable: "Crags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RouteOpeners",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteOpeners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteOpeners_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RouteRouteOpeners",
                schema: "core",
                columns: table => new
                {
                    RouteId = table.Column<int>(type: "integer", nullable: false),
                    OpenerId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteRouteOpeners", x => new { x.RouteId, x.OpenerId });
                    table.ForeignKey(
                        name: "FK_RouteRouteOpeners_RouteOpeners_OpenerId",
                        column: x => x.OpenerId,
                        principalSchema: "core",
                        principalTable: "RouteOpeners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RouteRouteOpeners_Routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "core",
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // CragId starts nullable so it can be backfilled below before being made required —
            // existing rows must get a real crag before the NOT NULL constraint is safe to add.
            migrationBuilder.AddColumn<int>(
                name: "CragId",
                schema: "core",
                table: "Routes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gear",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Orientation",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: true);

            // Backfill: the old free-text "Crag" becomes one flat, top-level Crag row per distinct
            // name (a string can't tell us the sector/subsector hierarchy — reparent these into a
            // tree afterward via the route form's "add sector under X" picker). The old free-text
            // "FirstAscent" becomes one shared RouteOpener per distinct string (not split into
            // individual names/years, since the source text isn't structured).
            migrationBuilder.Sql(
                """
                INSERT INTO core."Crags" ("Name")
                SELECT DISTINCT "Crag" FROM core."Routes";

                UPDATE core."Routes" r
                SET "CragId" = c."Id"
                FROM core."Crags" c
                WHERE c."Name" = r."Crag";

                INSERT INTO core."RouteOpeners" ("Name")
                SELECT DISTINCT "FirstAscent" FROM core."Routes"
                WHERE "FirstAscent" IS NOT NULL AND "FirstAscent" <> '';

                INSERT INTO core."RouteRouteOpeners" ("RouteId", "OpenerId")
                SELECT r."Id", o."Id"
                FROM core."Routes" r
                JOIN core."RouteOpeners" o ON o."Name" = r."FirstAscent"
                WHERE r."FirstAscent" IS NOT NULL AND r."FirstAscent" <> '';
                """);

            migrationBuilder.AlterColumn<int>(
                name: "CragId",
                schema: "core",
                table: "Routes",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_Routes_Crag",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "Crag",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "FirstAscent",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "GradeSystem",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "Type",
                schema: "core",
                table: "Routes");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_CragId",
                schema: "core",
                table: "Routes",
                column: "CragId");

            migrationBuilder.CreateIndex(
                name: "IX_Crags_Name",
                schema: "core",
                table: "Crags",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Crags_ParentCragId",
                schema: "core",
                table: "Crags",
                column: "ParentCragId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteOpeners_Name",
                schema: "core",
                table: "RouteOpeners",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_RouteOpeners_UserId",
                schema: "core",
                table: "RouteOpeners",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteRouteOpeners_OpenerId",
                schema: "core",
                table: "RouteRouteOpeners",
                column: "OpenerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Routes_Crags_CragId",
                schema: "core",
                table: "Routes",
                column: "CragId",
                principalSchema: "core",
                principalTable: "Crags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Routes_Crags_CragId",
                schema: "core",
                table: "Routes");

            migrationBuilder.AddColumn<string>(
                name: "Crag",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FirstAscent",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GradeSystem",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Restore what we can from the normalized tables before dropping them — otherwise every
            // migrated route silently loses its crag and opener info on rollback. GradeSystem/Type
            // stay empty: that data was already discarded for good in Up(), nothing to recover.
            migrationBuilder.Sql(
                """
                UPDATE core."Routes" r
                SET "Crag" = c."Name"
                FROM core."Crags" c
                WHERE c."Id" = r."CragId";

                UPDATE core."Routes" r
                SET "FirstAscent" = sub."Names"
                FROM (
                    SELECT rro."RouteId", string_agg(o."Name", ', ') AS "Names"
                    FROM core."RouteRouteOpeners" rro
                    JOIN core."RouteOpeners" o ON o."Id" = rro."OpenerId"
                    GROUP BY rro."RouteId"
                ) sub
                WHERE sub."RouteId" = r."Id";
                """);

            migrationBuilder.DropTable(
                name: "RouteRouteOpeners",
                schema: "core");

            migrationBuilder.DropTable(
                name: "RouteOpeners",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Crags",
                schema: "core");

            migrationBuilder.DropIndex(
                name: "IX_Routes_CragId",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "CragId",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "Gear",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "Orientation",
                schema: "core",
                table: "Routes");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_Crag",
                schema: "core",
                table: "Routes",
                column: "Crag");
        }
    }
}
