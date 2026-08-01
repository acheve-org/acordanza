using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Acordanza.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFirstAscensionistLinkRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FirstAscensionistLinkRequests",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirstAscensionistId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "text", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirstAscensionistLinkRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirstAscensionistLinkRequests_AspNetUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirstAscensionistLinkRequests_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FirstAscensionistLinkRequests_FirstAscensionists_FirstAscen~",
                        column: x => x.FirstAscensionistId,
                        principalSchema: "core",
                        principalTable: "FirstAscensionists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirstAscensionistLinkRequests_DecidedByUserId",
                schema: "core",
                table: "FirstAscensionistLinkRequests",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FirstAscensionistLinkRequests_FirstAscensionistId_UserId",
                schema: "core",
                table: "FirstAscensionistLinkRequests",
                columns: new[] { "FirstAscensionistId", "UserId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_FirstAscensionistLinkRequests_UserId",
                schema: "core",
                table: "FirstAscensionistLinkRequests",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FirstAscensionistLinkRequests",
                schema: "core");
        }
    }
}
