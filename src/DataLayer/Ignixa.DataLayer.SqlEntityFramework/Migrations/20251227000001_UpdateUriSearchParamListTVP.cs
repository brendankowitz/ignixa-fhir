using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Avoid constant arrays as arguments - generated migration code
#pragma warning disable IDE0161 // Use file-scoped namespace - generated migration code

namespace Ignixa.DataLayer.SqlEntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUriSearchParamListTVP : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TVPs (Table-Valued Parameters) cannot be altered in SQL Server
            // We must drop and recreate them
            // First drop the dependent stored procedure, then the TVP, then recreate both

            migrationBuilder.Sql(@"
                -- Drop the stored procedure that uses UriSearchParamList TVP
                IF OBJECT_ID('dbo.MergeResources', 'P') IS NOT NULL
                BEGIN
                    DROP PROCEDURE dbo.MergeResources;
                END
            ");

            migrationBuilder.Sql(@"
                -- Drop and recreate UriSearchParamList TVP with new columns
                IF TYPE_ID('dbo.UriSearchParamList') IS NOT NULL
                BEGIN
                    DROP TYPE dbo.UriSearchParamList;
                END
            ");

            migrationBuilder.Sql(@"
                -- Recreate UriSearchParamList TVP with Version and Fragment columns
                CREATE TYPE dbo.UriSearchParamList AS TABLE (
                    ResourceTypeId      SMALLINT      NOT NULL,
                    ResourceSurrogateId BIGINT        NOT NULL,
                    SearchParamId       SMALLINT      NOT NULL,
                    Uri                 VARCHAR(256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
                    Version             NVARCHAR(64)  NULL,
                    Fragment            NVARCHAR(128) NULL,
                    PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
                );
            ");

            // The MergeResources procedure will need to be recreated
            // This should be done in the application's 97.sql resource file
            // For now, we'll note that the procedure needs manual recreation
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback: recreate the original TVP without Version and Fragment
            migrationBuilder.Sql(@"
                IF OBJECT_ID('dbo.MergeResources', 'P') IS NOT NULL
                BEGIN
                    DROP PROCEDURE dbo.MergeResources;
                END
            ");

            migrationBuilder.Sql(@"
                IF TYPE_ID('dbo.UriSearchParamList') IS NOT NULL
                BEGIN
                    DROP TYPE dbo.UriSearchParamList;
                END
            ");

            migrationBuilder.Sql(@"
                CREATE TYPE dbo.UriSearchParamList AS TABLE (
                    ResourceTypeId      SMALLINT      NOT NULL,
                    ResourceSurrogateId BIGINT        NOT NULL,
                    SearchParamId       SMALLINT      NOT NULL,
                    Uri                 VARCHAR(256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
                    PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
                );
            ");
        }
    }
}
