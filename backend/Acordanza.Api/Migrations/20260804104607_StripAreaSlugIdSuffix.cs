using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Api.Migrations
{
    /// <inheritdoc />
    public partial class StripAreaSlugIdSuffix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Area.Slug used to be "{slugified name}-{Id}" (see AreasController.CreateArea);
            // the id suffix is dropped from newly created areas, so backfill existing rows
            // to match — matching only the row's own trailing "-{Id}" keeps this safe even
            // for area names that legitimately end in other digits.
            migrationBuilder.Sql(
                """
                UPDATE core."Areas"
                SET "Slug" = regexp_replace("Slug", '-' || "Id"::text || '$', '')
                WHERE "Slug" ~ ('-' || "Id"::text || '$');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE core."Areas"
                SET "Slug" = "Slug" || '-' || "Id"::text
                WHERE "Slug" !~ ('-' || "Id"::text || '$');
                """);
        }
    }
}
