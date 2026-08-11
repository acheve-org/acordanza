using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClimbingRouteSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                schema: "core",
                table: "Routes",
                type: "character varying(280)",
                maxLength: 280,
                nullable: false,
                defaultValue: "");

            // Backfill every existing row before the unique index below can apply. Mirrors
            // Slugify.ToSlug (lowercase, strip Spanish/Latin-1 accents, collapse
            // non-alphanumerics, trim "-"), falling back to "sector" for a name that
            // slugifies to nothing — translate() only covers Latin-1 accents, not all of
            // Unicode, which is fine for the current handful of Spanish names; renaming a
            // route or moving it to a new area regenerates its slug properly via
            // GenerateUniqueRouteSlugAsync.
            migrationBuilder.Sql(
                """
                UPDATE core."Routes"
                SET "Slug" = COALESCE(NULLIF(trim(both '-' from regexp_replace(
                    lower(translate("Name",
                        'áàäâãéèëêíìïîóòöôõúùüûñçÁÀÄÂÃÉÈËÊÍÌÏÎÓÒÖÔÕÚÙÜÛÑÇ',
                        'aaaaaeeeeiiiiooooouuuuncAAAAAEEEEIIIIOOOOOUUUUNC')),
                    '[^a-z0-9]+', '-', 'g')), ''), 'sector');
                """);

            // Two routes in the same area can share a Name (no uniqueness constraint on
            // it) so the backfill above can produce duplicate slugs within an area —
            // disambiguate in id order the same way GenerateUniqueRouteSlugAsync does
            // going forward (append "-2", "-3", ...) before the unique index below can
            // apply. Partitioned by AreaId, unlike FirstAscensionist's ungrouped version,
            // since this slug is scoped per-area, not global.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "AreaId", "Slug" ORDER BY "Id") AS rn
                    FROM core."Routes"
                )
                UPDATE core."Routes" o
                SET "Slug" = o."Slug" || '-' || ranked.rn::text
                FROM ranked
                WHERE ranked."Id" = o."Id" AND ranked.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Routes_AreaId_Slug",
                schema: "core",
                table: "Routes",
                columns: new[] { "AreaId", "Slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Routes_AreaId_Slug",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "Slug",
                schema: "core",
                table: "Routes");
        }
    }
}
