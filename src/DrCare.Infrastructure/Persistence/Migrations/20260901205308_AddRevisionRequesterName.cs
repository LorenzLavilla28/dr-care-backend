using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionRequesterName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevisionRequestedByName",
                table: "Contracts",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Contracts" AS contract
                SET "RevisionRequestedByName" = "User"."DisplayName"
                FROM "Users" AS "User"
                WHERE contract."RevisionRequestedBy" = "User"."Id"
                  AND contract."RevisionRequestedByName" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevisionRequestedByName",
                table: "Contracts");
        }
    }
}
