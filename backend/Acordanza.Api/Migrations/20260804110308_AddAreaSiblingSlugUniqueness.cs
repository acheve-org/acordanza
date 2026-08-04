using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaSiblingSlugUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Slugs were previously disambiguated only by the "-{id}" suffix stripped in
            // StripAreaSlugIdSuffix; two siblings could now share a slug (e.g. names that
            // differ only in diacritics/punctuation). Disambiguate any such existing
            // duplicates the same way AreasController.CreateArea does going forward
            // (append "-2", "-3", ... in id order) before the unique index below can apply.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY COALESCE("ParentAreaId", -1), "Slug" ORDER BY "Id"
                    ) AS rn
                    FROM core."Areas"
                )
                UPDATE core."Areas" a
                SET "Slug" = a."Slug" || '-' || ranked.rn::text
                FROM ranked
                WHERE ranked."Id" = a."Id" AND ranked.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Areas_ParentAreaId_Slug",
                schema: "core",
                table: "Areas",
                columns: new[] { "ParentAreaId", "Slug" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Areas_ParentAreaId_Slug",
                schema: "core",
                table: "Areas");
        }
    }
}
