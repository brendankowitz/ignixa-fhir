using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Avoid constant arrays as arguments - generated migration code
#pragma warning disable IDE0161 // Use file-scoped namespace - generated migration code

namespace Ignixa.DataLayer.SqlEntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddBackgroundJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: 97.sql creates the base schema (Resource, ResourceType, SearchParam tables, etc.)
            // This migration only creates objects NOT in 97.sql (BackgroundJobs table)

            // BackgroundJobs table is NEW (not in 97.sql) - added for background job orchestration
            migrationBuilder.CreateTable(
                name: "BackgroundJobs",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    JobId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    JobType = table.Column<int>(type: "int", nullable: false),
                    OrchestrationInstanceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Definition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Progress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HeartbeatDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Worker = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelRequested = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJobs", x => new { x.TenantId, x.JobId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_CreateDate",
                table: "BackgroundJobs",
                column: "CreateDate");

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_HeartbeatDate",
                table: "BackgroundJobs",
                column: "HeartbeatDate");

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_OrchestrationInstanceId",
                table: "BackgroundJobs",
                column: "OrchestrationInstanceId",
                filter: "[OrchestrationInstanceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_TenantId_JobType",
                table: "BackgroundJobs",
                columns: new[] { "TenantId", "JobType" });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_TenantId_Status",
                table: "BackgroundJobs",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only drop BackgroundJobs table (the only table this migration creates)
            migrationBuilder.DropTable(
                name: "BackgroundJobs");
        }
    }
}
#pragma warning restore CA1861
