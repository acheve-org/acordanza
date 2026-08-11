using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acordanza.Core.Migrations
{
    /// <inheritdoc />
    public partial class ValidationRejectionAndContributors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Areas_AspNetUsers_ValidatedByUserId",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropForeignKey(
                name: "FK_Routes_AspNetUsers_ValidatedByUserId",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Routes_ValidatedByUserId",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Areas_ValidatedByUserId",
                schema: "core",
                table: "Areas");

            // Scaffolded rename target hand-corrected (same documented-exception treatment
            // AddUniqueEmailAndPasskeys got): the diff engine matched old ValidatedByUserId
            // to the new RejectionReason column by coincidence (both nullable text), which
            // would have silently moved every existing decider's user id into a rejection
            // reason column instead of preserving it as DecidedByUserId. RejectionReason is
            // a genuinely new column below (AddColumn), not a rename.
            migrationBuilder.RenameColumn(
                name: "ValidatedByUserId",
                schema: "core",
                table: "Routes",
                newName: "DecidedByUserId");

            migrationBuilder.RenameColumn(
                name: "ValidatedAt",
                schema: "core",
                table: "Routes",
                newName: "DecidedAt");

            migrationBuilder.RenameColumn(
                name: "ValidatedByUserId",
                schema: "core",
                table: "Areas",
                newName: "DecidedByUserId");

            migrationBuilder.RenameColumn(
                name: "ValidatedAt",
                schema: "core",
                table: "Areas",
                newName: "DecidedAt");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "core",
                table: "Routes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationStatus",
                schema: "core",
                table: "Routes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AddedByUserId",
                schema: "core",
                table: "FirstAscensionists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "core",
                table: "FirstAscensionists",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DecidedAt",
                schema: "core",
                table: "FirstAscensionists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecidedByUserId",
                schema: "core",
                table: "FirstAscensionists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "core",
                table: "FirstAscensionists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationStatus",
                schema: "core",
                table: "FirstAscensionists",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AddedByUserId",
                schema: "core",
                table: "Areas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "core",
                table: "Areas",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "core",
                table: "Areas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationStatus",
                schema: "core",
                table: "Areas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // ValidationStatus's AddColumn default ("") isn't a valid ReviewStatus name, so
            // every pre-existing row must be backfilled explicitly before the app can read
            // it back through the string-converted enum property. Routes/Areas: a prior
            // decision (DecidedAt already renamed above) means Approved, otherwise Pending.
            // FirstAscensionists: brand new lifecycle, so every existing row starts Pending —
            // nobody has reviewed them yet.
            migrationBuilder.Sql(
                """
                UPDATE "core"."Routes" SET "ValidationStatus" = CASE WHEN "DecidedAt" IS NOT NULL THEN 'Approved' ELSE 'Pending' END;
                UPDATE "core"."Areas" SET "ValidationStatus" = CASE WHEN "DecidedAt" IS NOT NULL THEN 'Approved' ELSE 'Pending' END;
                UPDATE "core"."FirstAscensionists" SET "ValidationStatus" = 'Pending';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Routes_DecidedByUserId",
                schema: "core",
                table: "Routes",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FirstAscensionists_AddedByUserId",
                schema: "core",
                table: "FirstAscensionists",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FirstAscensionists_DecidedByUserId",
                schema: "core",
                table: "FirstAscensionists",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Areas_AddedByUserId",
                schema: "core",
                table: "Areas",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Areas_DecidedByUserId",
                schema: "core",
                table: "Areas",
                column: "DecidedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Areas_AspNetUsers_AddedByUserId",
                schema: "core",
                table: "Areas",
                column: "AddedByUserId",
                principalSchema: "identity",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Areas_AspNetUsers_DecidedByUserId",
                schema: "core",
                table: "Areas",
                column: "DecidedByUserId",
                principalSchema: "identity",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FirstAscensionists_AspNetUsers_AddedByUserId",
                schema: "core",
                table: "FirstAscensionists",
                column: "AddedByUserId",
                principalSchema: "identity",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FirstAscensionists_AspNetUsers_DecidedByUserId",
                schema: "core",
                table: "FirstAscensionists",
                column: "DecidedByUserId",
                principalSchema: "identity",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Routes_AspNetUsers_DecidedByUserId",
                schema: "core",
                table: "Routes",
                column: "DecidedByUserId",
                principalSchema: "identity",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Areas_AspNetUsers_AddedByUserId",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropForeignKey(
                name: "FK_Areas_AspNetUsers_DecidedByUserId",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropForeignKey(
                name: "FK_FirstAscensionists_AspNetUsers_AddedByUserId",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropForeignKey(
                name: "FK_FirstAscensionists_AspNetUsers_DecidedByUserId",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropForeignKey(
                name: "FK_Routes_AspNetUsers_DecidedByUserId",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Routes_DecidedByUserId",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_FirstAscensionists_AddedByUserId",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropIndex(
                name: "IX_FirstAscensionists_DecidedByUserId",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropIndex(
                name: "IX_Areas_AddedByUserId",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropIndex(
                name: "IX_Areas_DecidedByUserId",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "ValidationStatus",
                schema: "core",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "AddedByUserId",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropColumn(
                name: "DecidedByUserId",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropColumn(
                name: "ValidationStatus",
                schema: "core",
                table: "FirstAscensionists");

            migrationBuilder.DropColumn(
                name: "AddedByUserId",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "core",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "ValidationStatus",
                schema: "core",
                table: "Areas");

            migrationBuilder.RenameColumn(
                name: "DecidedByUserId",
                schema: "core",
                table: "Routes",
                newName: "ValidatedByUserId");

            migrationBuilder.RenameColumn(
                name: "DecidedAt",
                schema: "core",
                table: "Routes",
                newName: "ValidatedAt");

            migrationBuilder.RenameColumn(
                name: "DecidedByUserId",
                schema: "core",
                table: "Areas",
                newName: "ValidatedByUserId");

            migrationBuilder.RenameColumn(
                name: "DecidedAt",
                schema: "core",
                table: "Areas",
                newName: "ValidatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_ValidatedByUserId",
                schema: "core",
                table: "Routes",
                column: "ValidatedByUserId");

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
    }
}
