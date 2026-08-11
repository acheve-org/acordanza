using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Acordanza.Core.Migrations
{
    /// <inheritdoc />
    public partial class RouteFieldsAndLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tags",
                schema: "core",
                table: "Routes");

            // Existing rows backfill to 100 (see plan 16 §1); the default is dropped
            // immediately after so a future omitted Length fails loudly instead of
            // silently landing on 100 — EF's model carries no HasDefaultValue for this
            // column, unlike ApplicationUser.Name/Area.Slug, whose defaults stay in
            // place because Program.cs backfills those separately at startup.
            migrationBuilder.AddColumn<int>(
                name: "Length",
                schema: "core",
                table: "Routes",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.Sql(@"ALTER TABLE core.""Routes"" ALTER COLUMN ""Length"" DROP DEFAULT;");

            migrationBuilder.AddColumn<string>(
                name: "ObligatoryGrade",
                schema: "core",
                table: "Routes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionalGear",
                schema: "core",
                table: "Routes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecommendedGear",
                schema: "core",
                table: "Routes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Links",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RouteId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Links_Routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "core",
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Links_RouteId",
                schema: "core",
                table: "Links",
                column: "RouteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Links",
                schema: "core");

            migrationBuilder.DropColumn(
                name: "Length",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "ObligatoryGrade",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "OptionalGear",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "RecommendedGear",
                schema: "core",
                table: "Routes");

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
