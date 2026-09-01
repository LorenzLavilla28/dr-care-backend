using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistContractRevisionContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevisionReason",
                table: "Contracts",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevisionRequestedAt",
                table: "Contracts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevisionRequestedBy",
                table: "Contracts",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevisionReason",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "RevisionRequestedAt",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "RevisionRequestedBy",
                table: "Contracts");
        }
    }
}
