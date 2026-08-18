using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Core.Migrations
{
    /// <inheritdoc />
    public partial class OptionalRouteGradeAndLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Length",
                schema: "core",
                table: "Routes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Grade",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "AidGrade",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: true);

            // Length was a non-nullable int, and the import worker wrote 0 to mean "the
            // source didn't state one" (RouteImportService.AcceptableLength, plan 30). Now
            // that the column can say "unknown" properly, the sentinel has to go —
            // otherwise 0 and NULL both mean unknown forever and every reader has to know
            // about both. 0 is not a real length: the only writer that could produce it is
            // that sentinel, since ValidateLength's floor has always been > 5.
            migrationBuilder.Sql("""UPDATE core."Routes" SET "Length" = NULL WHERE "Length" = 0;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AidGrade",
                schema: "core",
                table: "Routes");

            migrationBuilder.AlterColumn<int>(
                name: "Length",
                schema: "core",
                table: "Routes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Grade",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
