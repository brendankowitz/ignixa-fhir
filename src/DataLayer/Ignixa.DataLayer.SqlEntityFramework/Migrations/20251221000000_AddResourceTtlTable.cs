using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Avoid constant arrays as arguments - generated migration code
#pragma warning disable IDE0161 // Use file-scoped namespace - generated migration code

namespace Ignixa.DataLayer.SqlEntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceTtlTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create ResourceTtl table for TTL (Time-To-Live) feature
            // Separate table design allows efficient tracking of expiring resources
            // without adding nullable column to massive Resource table
            migrationBuilder.CreateTable(
                name: "ResourceTtl",
                schema: "dbo",
                columns: table => new
                {
                    ResourceTypeId = table.Column<short>(type: "smallint", nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceTtl", x => new { x.ResourceTypeId, x.ResourceId });
                });

            // Create index on ExpiresAt for efficient cleanup queries
            // TtlCleanupService queries: WHERE ExpiresAt < now
            migrationBuilder.CreateIndex(
                name: "IX_ResourceTtl_ExpiresAt",
                schema: "dbo",
                table: "ResourceTtl",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop ResourceTtl table
            migrationBuilder.DropTable(
                name: "ResourceTtl",
                schema: "dbo");
        }
    }
}
#pragma warning restore CA1861
