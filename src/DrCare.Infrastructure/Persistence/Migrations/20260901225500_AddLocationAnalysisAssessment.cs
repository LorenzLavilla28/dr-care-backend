using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationAnalysisAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "LocationAnalyses",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessmentJson",
                table: "LocationAnalyses",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwnershipStatus",
                table: "LocationAnalyses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "LocationAnalyses",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevisionReason",
                table: "LocationAnalyses",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedAt",
                table: "LocationAnalyses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedBy",
                table: "LocationAnalyses",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssessmentJson",
                table: "LocationAnalyses");

            migrationBuilder.DropColumn(
                name: "LeaseOwnershipStatus",
                table: "LocationAnalyses");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                table: "LocationAnalyses");

            migrationBuilder.DropColumn(
                name: "RevisionReason",
                table: "LocationAnalyses");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "LocationAnalyses");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "LocationAnalyses");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "LocationAnalyses",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
