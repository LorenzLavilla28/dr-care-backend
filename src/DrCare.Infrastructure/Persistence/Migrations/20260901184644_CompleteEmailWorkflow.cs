using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteEmailWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InvitationEmailId",
                table: "ContractSigningRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReminderEmailId",
                table: "ContractSigningRequests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvitationEmailId",
                table: "ContractSigningRequests");

            migrationBuilder.DropColumn(
                name: "ReminderEmailId",
                table: "ContractSigningRequests");
        }
    }
}
