using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFirstAscensionistSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                schema: "core",
                table: "FirstAscensionists",
                type: "character varying(280)",
                maxLength: 280,
                nullable: false,
                defaultValue: "");

            // Backfill every existing row before the unique index below can apply. Mirrors
            // Slugify.ToSlug (lowercase, strip Spanish/Latin-1 accents, collapse
            // non-alphanumerics, trim "-"), falling back to "aperturista" for a name that
            // slugifies to nothing — translate() only covers Latin-1 accents, not all of
            // Unicode, which is fine for the current handful of Spanish names; renaming a
            // record regenerates its slug properly via GenerateUniqueSlugAsync.
            migrationBuilder.Sql(
                """
                UPDATE core."FirstAscensionists"
                SET "Slug" = COALESCE(NULLIF(trim(both '-' from regexp_replace(
                    lower(translate("Name",
                        'áàäâãéèëêíìïîóòöôõúùüûñçÁÀÄÂÃÉÈËÊÍÌÏÎÓÒÖÔÕÚÙÜÛÑÇ',
                        'aaaaaeeeeiiiiooooouuuuncAAAAAEEEEIIIIOOOOOUUUUNC')),
                    '[^a-z0-9]+', '-', 'g')), ''), 'aperturista');
                """);

            // Two first ascensionists can share a Name (unlike Area, which enforces
            // uniqueness per parent) so the backfill above can produce duplicate slugs —
            // disambiguate in id order the same way GenerateUniqueSlugAsync does going
            // forward (append "-2", "-3", ...) before the unique index below can apply.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Slug" ORDER BY "Id") AS rn
                    FROM core."FirstAscensionists"
                )
                UPDATE core."FirstAscensionists" o
                SET "Slug" = o."Slug" || '-' || ranked.rn::text
                FROM ranked
                WHERE ranked."Id" = o."Id" AND ranked.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FirstAscensionists_Slug",
                schema: "core",
                table: "FirstAscensionists",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FirstAscensionists_Slug",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropColumn(
                name: "Slug",
                schema: "core",
                table: "FirstAscensionists");
        }
    }
}
