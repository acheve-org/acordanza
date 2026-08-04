using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidatedAt",
                schema: "core",
                table: "Areas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidatedByUserId",
                schema: "core",
                table: "Areas",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Areas_ValidatedByUserId",
                schema: "core",
                table: "Areas",
                column: "ValidatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Areas_AspNetUsers_ValidatedByUserId",
                schema: "core",
                table: "Areas",
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
                name: "FK_Areas_AspNetUsers_ValidatedByUserId",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropIndex(
                name: "IX_Areas_ValidatedByUserId",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "ValidatedAt",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "ValidatedByUserId",
                schema: "core",
                table: "Areas");
        }
    }
}
