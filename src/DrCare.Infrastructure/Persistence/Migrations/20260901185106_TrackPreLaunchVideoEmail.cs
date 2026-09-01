using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackPreLaunchVideoEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VideoEmailId",
                table: "PreLaunchChecklists",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VideoQueuedAt",
                table: "PreLaunchChecklists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoUrl",
                table: "PreLaunchChecklists",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoEmailId",
                table: "PreLaunchChecklists");

            migrationBuilder.DropColumn(
                name: "VideoQueuedAt",
                table: "PreLaunchChecklists");

            migrationBuilder.DropColumn(
                name: "VideoUrl",
                table: "PreLaunchChecklists");
        }
    }
}
