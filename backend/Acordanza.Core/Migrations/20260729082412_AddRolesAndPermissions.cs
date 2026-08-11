using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidatedAt",
                schema: "core",
                table: "Routes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidatedByUserId",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routes_ValidatedByUserId",
                schema: "core",
                table: "Routes",
                column: "ValidatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Routes_AspNetUsers_ValidatedByUserId",
                schema: "core",
                table: "Routes",
                column: "ValidatedByUserId",
                principalSchema: "identity",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Routes_AspNetUsers_ValidatedByUserId",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Routes_ValidatedByUserId",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "ValidatedAt",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "ValidatedByUserId",
                schema: "core",
                table: "Routes");
        }
    }
}
