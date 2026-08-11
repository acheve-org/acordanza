using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CentroidLat",
                schema: "core",
                table: "Areas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CentroidLng",
                schema: "core",
                table: "Areas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Polygon",
                schema: "core",
                table: "Areas",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CentroidLat",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "CentroidLng",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "Polygon",
                schema: "core",
                table: "Areas");
        }
    }
}
