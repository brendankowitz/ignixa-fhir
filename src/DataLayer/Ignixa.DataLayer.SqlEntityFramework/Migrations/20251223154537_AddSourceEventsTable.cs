using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ignixa.DataLayer.SqlEntityFramework.Migrations;

/// <inheritdoc />
public partial class AddSourceEventsTable : Migration
{
    private static readonly string[] StreamIdEventIdColumns = ["StreamId", "EventId"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SourceEvents",
            columns: table => new
            {
                EventId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                StreamId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                EventData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                TransactionId = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SourceEvents", x => x.EventId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SourceEvents_StreamId_EventId",
            table: "SourceEvents",
            columns: StreamIdEventIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_SourceEvents_EventId",
            table: "SourceEvents",
            column: "EventId");

        migrationBuilder.CreateIndex(
            name: "IX_SourceEvents_TransactionId",
            table: "SourceEvents",
            column: "TransactionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SourceEvents");
    }
}
