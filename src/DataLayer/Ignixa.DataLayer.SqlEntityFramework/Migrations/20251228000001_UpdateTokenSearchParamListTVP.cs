using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Avoid constant arrays as arguments - generated migration code
#pragma warning disable IDE0161 // Use file-scoped namespace - generated migration code

namespace Ignixa.DataLayer.SqlEntityFramework.Migrations
{
    /// <summary>
    /// Updates TokenSearchParamList TVP to include IdentifierTypeSystemId and IdentifierTypeCode columns
    /// for supporting the FHIR identifier:of-type search modifier.
    /// </summary>
    public partial class UpdateTokenSearchParamListTVP : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TVPs (Table-Valued Parameters) cannot be altered in SQL Server
            // We must drop and recreate them
            // First drop the dependent stored procedure, then the TVP, then recreate both

            migrationBuilder.Sql(@"
                -- Drop the stored procedure that uses TokenSearchParamList TVP
                IF OBJECT_ID('dbo.MergeResources', 'P') IS NOT NULL
                BEGIN
                    DROP PROCEDURE dbo.MergeResources;
                END
            ");

            migrationBuilder.Sql(@"
                -- Drop TokenSearchParamList TVP
                IF TYPE_ID('dbo.TokenSearchParamList') IS NOT NULL
                BEGIN
                    DROP TYPE dbo.TokenSearchParamList;
                END
            ");

            migrationBuilder.Sql(@"
                -- Recreate TokenSearchParamList TVP with IdentifierTypeSystemId and IdentifierTypeCode columns
                CREATE TYPE dbo.TokenSearchParamList AS TABLE (
                    ResourceTypeId          SMALLINT      NOT NULL,
                    ResourceSurrogateId     BIGINT        NOT NULL,
                    SearchParamId           SMALLINT      NOT NULL,
                    SystemId                INT           NULL,
                    Code                    VARCHAR(256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
                    CodeOverflow            VARCHAR(MAX)  NULL,
                    IdentifierTypeSystemId  INT           NULL,
                    IdentifierTypeCode      VARCHAR(256)  COLLATE Latin1_General_100_CS_AS NULL
                );
            ");

            // The MergeResources procedure will need to be recreated
            // This should be done in the application's SQL resource files
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback: recreate the original TVP without IdentifierType columns
            migrationBuilder.Sql(@"
                IF OBJECT_ID('dbo.MergeResources', 'P') IS NOT NULL
                BEGIN
                    DROP PROCEDURE dbo.MergeResources;
                END
            ");

            migrationBuilder.Sql(@"
                IF TYPE_ID('dbo.TokenSearchParamList') IS NOT NULL
                BEGIN
                    DROP TYPE dbo.TokenSearchParamList;
                END
            ");

            migrationBuilder.Sql(@"
                CREATE TYPE dbo.TokenSearchParamList AS TABLE (
                    ResourceTypeId      SMALLINT      NOT NULL,
                    ResourceSurrogateId BIGINT        NOT NULL,
                    SearchParamId       SMALLINT      NOT NULL,
                    SystemId            INT           NULL,
                    Code                VARCHAR(256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
                    CodeOverflow        VARCHAR(MAX)  NULL
                );
            ");
        }
    }
}
