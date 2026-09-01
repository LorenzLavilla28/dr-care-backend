using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillContractRevisionContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Contracts" AS contract
                SET "RevisionReason" = LEFT(regexp_replace(activity."Message", '^Contract returned for revision:[[:space:]]*', '', 'i'), 4000),
                    "RevisionRequestedAt" = activity."CreatedAt",
                    "RevisionRequestedBy" = activity."ActorId"
                FROM (
                    SELECT DISTINCT ON ("OrganizationId", "LeadId")
                           "OrganizationId", "LeadId", "Message", "CreatedAt", "ActorId"
                    FROM "ActivityLogs"
                    WHERE "Type" = 'ContractRevisionRequested'
                    ORDER BY "OrganizationId", "LeadId", "CreatedAt" DESC
                ) AS activity
                WHERE contract."OrganizationId" = activity."OrganizationId"
                  AND contract."LeadId" = activity."LeadId"
                  AND contract."Status" = 'RevisionRequested'
                  AND contract."RevisionReason" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
