using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteFranchisingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Paused",
                table: "PreLaunchItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Leads",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoodTimeToDiscuss",
                table: "Leads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "Leads",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastCallOutcome",
                table: "Leads",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeadSource",
                table: "Leads",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MeetingDateTime",
                table: "Leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionsConcerns",
                table: "Leads",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WelcomeEmailReceived",
                table: "Leads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryJson",
                table: "Endorsements",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvoiceDueAt",
                table: "DownPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceObjectKey",
                table: "DownPayments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceSha256",
                table: "DownPayments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentPdfObjectKey",
                table: "Contracts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenderedHtml",
                table: "Contracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedPdfObjectKey",
                table: "Contracts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedPdfSha256",
                table: "Contracts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractSigningRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignerRole = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SignerName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SignerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ViewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeclinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeclineReason = table.Column<string>(type: "text", nullable: true),
                    SignatureSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SignedDocumentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SignedDocumentSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractSigningRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractSigningRequests_OrganizationId_ContractId_Status",
                table: "ContractSigningRequests",
                columns: new[] { "OrganizationId", "ContractId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractSigningRequests_TokenHash",
                table: "ContractSigningRequests",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractSigningRequests");

            migrationBuilder.DropColumn(
                name: "Paused",
                table: "PreLaunchItems");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "GoodTimeToDiscuss",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LastCallOutcome",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LeadSource",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "MeetingDateTime",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "QuestionsConcerns",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "WelcomeEmailReceived",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "BoundaryJson",
                table: "Endorsements");

            migrationBuilder.DropColumn(
                name: "InvoiceDueAt",
                table: "DownPayments");

            migrationBuilder.DropColumn(
                name: "InvoiceObjectKey",
                table: "DownPayments");

            migrationBuilder.DropColumn(
                name: "InvoiceSha256",
                table: "DownPayments");

            migrationBuilder.DropColumn(
                name: "CurrentPdfObjectKey",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "RenderedHtml",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SignedPdfObjectKey",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SignedPdfSha256",
                table: "Contracts");
        }
    }
}
