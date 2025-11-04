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
            migrationBuilder.EnsureSchema(
                name: "dbo");

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

            migrationBuilder.CreateTable(
                name: "QuantityCode",
                columns: table => new
                {
                    QuantityCodeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Value = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuantityCode", x => x.QuantityCodeId);
                });

            migrationBuilder.CreateTable(
                name: "ResourceType",
                schema: "dbo",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ResourceTypeId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PKC_ResourceType", x => x.Name)
                        .Annotation("SqlServer:Clustered", true);
                    table.UniqueConstraint("AK_ResourceType_ResourceTypeId", x => x.ResourceTypeId);
                });

            migrationBuilder.CreateTable(
                name: "SearchParam",
                columns: table => new
                {
                    SearchParamId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uri = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IsPartiallySupported = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchParam", x => x.SearchParamId);
                });

            migrationBuilder.CreateTable(
                name: "System",
                columns: table => new
                {
                    SystemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Value = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_System", x => x.SystemId);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                schema: "dbo",
                columns: table => new
                {
                    SurrogateIdRangeFirstValue = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurrogateIdRangeLastValue = table.Column<long>(type: "bigint", nullable: false),
                    Definition = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsSuccess = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsVisible = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsHistoryMoved = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "getUTCdate()"),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VisibleDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HistoryMovedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HeartbeatDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "getUTCdate()"),
                    FailureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsControlledByClient = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    InvisibleHistoryRemovedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PKC_Transactions_SurrogateIdRangeFirstValue", x => x.SurrogateIdRangeFirstValue);
                });

            migrationBuilder.CreateTable(
                name: "Resource",
                schema: "dbo",
                columns: table => new
                {
                    ResourceTypeId = table.Column<short>(type: "smallint", nullable: false),
                    ResourceSurrogateId = table.Column<long>(type: "bigint", nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsHistory = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RequestMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RawResource = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    IsRawResourceMetaSet = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    SearchParamHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true),
                    HistoryTransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PKC_Resource", x => new { x.ResourceTypeId, x.ResourceSurrogateId });
                    table.CheckConstraint("CH_Resource_RawResource_Length", "RawResource > 0x0");
                    table.ForeignKey(
                        name: "FK_Resource_ResourceType_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalSchema: "dbo",
                        principalTable: "ResourceType",
                        principalColumn: "ResourceTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Resource_Transactions_HistoryTransactionId",
                        column: x => x.HistoryTransactionId,
                        principalSchema: "dbo",
                        principalTable: "Transactions",
                        principalColumn: "SurrogateIdRangeFirstValue",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Resource_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalSchema: "dbo",
                        principalTable: "Transactions",
                        principalColumn: "SurrogateIdRangeFirstValue",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DateTimeSearchParam",
                schema: "dbo",
                columns: table => new
                {
                    ResourceTypeId = table.Column<short>(type: "smallint", nullable: false),
                    ResourceSurrogateId = table.Column<long>(type: "bigint", nullable: false),
                    SearchParamId = table.Column<short>(type: "smallint", nullable: false),
                    StartDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsLongerThanADay = table.Column<bool>(type: "bit", nullable: false),
                    IsMin = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsMax = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DateTimeSearchParam", x => new { x.ResourceTypeId, x.ResourceSurrogateId, x.SearchParamId, x.StartDateTime });
                    table.ForeignKey(
                        name: "FK_DateTimeSearchParam_Resource_ResourceTypeId_ResourceSurrogateId",
                        columns: x => new { x.ResourceTypeId, x.ResourceSurrogateId },
                        principalSchema: "dbo",
                        principalTable: "Resource",
                        principalColumns: new[] { "ResourceTypeId", "ResourceSurrogateId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NumberSearchParam",
                schema: "dbo",
                columns: table => new
                {
                    ResourceTypeId = table.Column<short>(type: "smallint", nullable: false),
                    ResourceSurrogateId = table.Column<long>(type: "bigint", nullable: false),
                    SearchParamId = table.Column<short>(type: "smallint", nullable: false),
                    SingleValue = table.Column<decimal>(type: "decimal(36,18)", precision: 36, scale: 18, nullable: true),
                    LowValue = table.Column<decimal>(type: "decimal(36,18)", precision: 36, scale: 18, nullable: false),
                    HighValue = table.Column<decimal>(type: "decimal(36,18)", precision: 36, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSearchParam", x => new { x.ResourceTypeId, x.ResourceSurrogateId, x.SearchParamId });
                    table.ForeignKey(
                        name: "FK_NumberSearchParam_Resource_ResourceTypeId_ResourceSurrogateId",
                        columns: x => new { x.ResourceTypeId, x.ResourceSurrogateId },
                        principalSchema: "dbo",
                        principalTable: "Resource",
                        principalColumns: new[] { "ResourceTypeId", "ResourceSurrogateId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuantitySearchParam",
                schema: "dbo",
                columns: table => new
                {
                    ResourceTypeId = table.Column<short>(type: "smallint", nullable: false),
                    ResourceSurrogateId = table.Column<long>(type: "bigint", nullable: false),
                    SearchParamId = table.Column<short>(type: "smallint", nullable: false),
                    SystemId = table.Column<int>(type: "int", nullable: true),
                    QuantityCodeId = table.Column<int>(type: "int", nullable: true),
                    SingleValue = table.Column<decimal>(type: "decimal(36,18)", precision: 36, scale: 18, nullable: true),
                    LowValue = table.Column<decimal>(type: "decimal(36,18)", precision: 36, scale: 18, nullable: false),
                    HighValue = table.Column<decimal>(type: "decimal(36,18)", precision: 36, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuantitySearchParam", x => new { x.ResourceTypeId, x.ResourceSurrogateId, x.SearchParamId });
                    table.ForeignKey(
                        name: "FK_QuantitySearchParam_Resource_ResourceTypeId_ResourceSurrogateId",
                        columns: x => new { x.ResourceTypeId, x.ResourceSurrogateId },
                        principalSchema: "dbo",
                        principalTable: "Resource",
                        principalColumns: new[] { "ResourceTypeId", "ResourceSurrogateId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReferenceSearchParam",
                schema: "dbo",
                columns: table => new
                {
                    ResourceTypeId = table.Column<short>(type: "smallint", nullable: false),
                    ResourceSurrogateId = table.Column<long>(type: "bigint", nullable: false),
                    SearchParamId = table.Column<short>(type: "smallint", nullable: false),
                    ReferenceResourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BaseUri = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReferenceResourceTypeId = table.Column<short>(type: "smallint", nullable: true),
                    ReferenceResourceVersion = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceSearchParam", x => new { x.ResourceTypeId, x.ResourceSurrogateId, x.SearchParamId, x.ReferenceResourceId });
                    table.ForeignKey(
                        name: "FK_ReferenceSearchParam_Resource_ResourceTypeId_ResourceSurrogateId",
                        columns: x => new { x.ResourceTypeId, x.ResourceSurrogateId },
                        principalSchema: "dbo",
                        principalTable: "Resource",
                        principalColumns: new[] { "ResourceTypeId", "ResourceSurrogateId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StringSearchParam",
                schema: "dbo",
                columns: table => new
                {
                    ResourceTypeId = table.Column<short>(type: "smallint", nullable: false),
                    ResourceSurrogateId = table.Column<long>(type: "bigint", nullable: false),
                    SearchParamId = table.Column<short>(type: "smallint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TextOverflow = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsMin = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsMax = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StringSearchParam", x => new { x.ResourceTypeId, x.ResourceSurrogateId, x.SearchParamId, x.Text });
                    table.ForeignKey(
                        name: "FK_StringSearchParam_Resource_ResourceTypeId_ResourceSurrogateId",
                        columns: x => new { x.ResourceTypeId, x.ResourceSurrogateId },
                        principalSchema: "dbo",
                        principalTable: "Resource",
                        principalColumns: new[] { "ResourceTypeId", "ResourceSurrogateId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TokenSearchParam",
                schema: "dbo",
                columns: table => new
                {
                    ResourceTypeId = table.Column<short>(type: "smallint", nullable: false),
                    ResourceSurrogateId = table.Column<long>(type: "bigint", nullable: false),
                    SearchParamId = table.Column<short>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SystemId = table.Column<int>(type: "int", nullable: true),
                    CodeOverflow = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenSearchParam", x => new { x.ResourceTypeId, x.ResourceSurrogateId, x.SearchParamId, x.Code });
                    table.CheckConstraint("CHK_TokenSearchParam_CodeOverflow", "LEN(Code) = 256 OR CodeOverflow IS NULL");
                    table.ForeignKey(
                        name: "FK_TokenSearchParam_Resource_ResourceTypeId_ResourceSurrogateId",
                        columns: x => new { x.ResourceTypeId, x.ResourceSurrogateId },
                        principalSchema: "dbo",
                        principalTable: "Resource",
                        principalColumns: new[] { "ResourceTypeId", "ResourceSurrogateId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UriSearchParam",
                schema: "dbo",
                columns: table => new
                {
                    ResourceTypeId = table.Column<short>(type: "smallint", nullable: false),
                    ResourceSurrogateId = table.Column<long>(type: "bigint", nullable: false),
                    SearchParamId = table.Column<short>(type: "smallint", nullable: false),
                    Uri = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UriSearchParam", x => new { x.ResourceTypeId, x.ResourceSurrogateId, x.SearchParamId, x.Uri });
                    table.ForeignKey(
                        name: "FK_UriSearchParam_Resource_ResourceTypeId_ResourceSurrogateId",
                        columns: x => new { x.ResourceTypeId, x.ResourceSurrogateId },
                        principalSchema: "dbo",
                        principalTable: "Resource",
                        principalColumns: new[] { "ResourceTypeId", "ResourceSurrogateId" },
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateIndex(
                name: "UQ_QuantityCode_Value",
                table: "QuantityCode",
                column: "Value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_ReferenceSearchParam",
                schema: "dbo",
                table: "ReferenceSearchParam",
                columns: new[] { "ResourceTypeId", "ResourceSurrogateId", "SearchParamId", "BaseUri", "ReferenceResourceTypeId", "ReferenceResourceId" },
                unique: true,
                filter: "[BaseUri] IS NOT NULL AND [ReferenceResourceTypeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Resource_HistoryTransactionId",
                schema: "dbo",
                table: "Resource",
                column: "HistoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Resource_ResourceTypeId_ResourceId",
                schema: "dbo",
                table: "Resource",
                columns: new[] { "ResourceTypeId", "ResourceId" },
                unique: true,
                filter: "[IsHistory] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Resource_ResourceTypeId_ResourceId_Version",
                schema: "dbo",
                table: "Resource",
                columns: new[] { "ResourceTypeId", "ResourceId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Resource_TransactionId",
                schema: "dbo",
                table: "Resource",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTypeId_HistoryTransactionId",
                schema: "dbo",
                table: "Resource",
                columns: new[] { "ResourceTypeId", "HistoryTransactionId" },
                filter: "[HistoryTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTypeId_TransactionId",
                schema: "dbo",
                table: "Resource",
                columns: new[] { "ResourceTypeId", "TransactionId" },
                filter: "[TransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_ResourceType_ResourceTypeId",
                schema: "dbo",
                table: "ResourceType",
                column: "ResourceTypeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_SearchParam_Uri",
                table: "SearchParam",
                column: "Uri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_System_Value",
                table: "System",
                column: "Value",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackgroundJobs");

            migrationBuilder.DropTable(
                name: "DateTimeSearchParam",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "NumberSearchParam",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QuantityCode");

            migrationBuilder.DropTable(
                name: "QuantitySearchParam",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ReferenceSearchParam",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SearchParam");

            migrationBuilder.DropTable(
                name: "StringSearchParam",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "System");

            migrationBuilder.DropTable(
                name: "TokenSearchParam",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "UriSearchParam",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Resource",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ResourceType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Transactions",
                schema: "dbo");
        }
    }
}
#pragma warning restore CA1861
