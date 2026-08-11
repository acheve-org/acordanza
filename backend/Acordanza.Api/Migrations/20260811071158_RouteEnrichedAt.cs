using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Api.Migrations
{
    /// <inheritdoc />
    public partial class RouteEnrichedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EnrichedAt",
                schema: "core",
                table: "Routes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnrichedAt",
                schema: "core",
                table: "Routes");
        }
    }
}
