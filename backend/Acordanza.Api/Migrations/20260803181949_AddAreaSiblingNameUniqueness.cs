using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaSiblingNameUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Areas_ParentAreaId",
                schema: "core",
                table: "Areas");

            migrationBuilder.CreateIndex(
                name: "IX_Areas_ParentAreaId_Name",
                schema: "core",
                table: "Areas",
                columns: new[] { "ParentAreaId", "Name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Areas_ParentAreaId_Name",
                schema: "core",
                table: "Areas");

            migrationBuilder.CreateIndex(
                name: "IX_Areas_ParentAreaId",
                schema: "core",
                table: "Areas",
                column: "ParentAreaId");
        }
    }
}
