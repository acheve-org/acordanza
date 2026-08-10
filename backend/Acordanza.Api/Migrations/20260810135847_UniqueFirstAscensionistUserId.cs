using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Api.Migrations
{
    /// <inheritdoc />
    public partial class UniqueFirstAscensionistUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FirstAscensionists_UserId",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.CreateIndex(
                name: "IX_FirstAscensionists_UserId",
                schema: "core",
                table: "FirstAscensionists",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FirstAscensionists_UserId",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.CreateIndex(
                name: "IX_FirstAscensionists_UserId",
                schema: "core",
                table: "FirstAscensionists",
                column: "UserId");
        }
    }
}
