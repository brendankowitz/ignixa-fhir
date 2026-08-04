using Ignixa.Search.Sql.Generators;

namespace Ignixa.Search.Sql.Generators.Tests;

public class DdlTableParserRealDdlTests
{
    [Fact]
    public void GivenRealStringSearchParamDdl_WhenParsed_ThenTextColumnMatchesHandVerifiedCatalog()
    {
        // Arrange -- copied verbatim from
        // src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/StringSearchParam.sql lines 1-9
        var ddl = """
            CREATE TABLE dbo.StringSearchParam (
                ResourceTypeId      SMALLINT       NOT NULL,
                ResourceSurrogateId BIGINT         NOT NULL,
                SearchParamId       SMALLINT       NOT NULL,
                Text                NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
                TextOverflow        NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL,
                IsMin               BIT            CONSTRAINT string_IsMin_Constraint DEFAULT 0 NOT NULL,
                IsMax               BIT            CONSTRAINT string_IsMax_Constraint DEFAULT 0 NOT NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam", StringComparison.Ordinal));
        var table = tables[0];

        // Assert -- matches SqlCatalogTests.GivenStringSearchParam_WhenLookedUp_ThenTextColumnMatchesRealDdl
        var text = table.Columns.Single(c => c.Name == "Text");
        text.SqlType.ShouldBe("nvarchar");
        text.MaxLength.ShouldBe(256);
        text.Collation.ShouldBe("Latin1_General_100_CI_AI_SC");
        text.IsNullable.ShouldBeFalse();

        var textOverflow = table.Columns.Single(c => c.Name == "TextOverflow");
        textOverflow.SqlType.ShouldBe("nvarchar");
        textOverflow.MaxLength.ShouldBeNull();
        textOverflow.Collation.ShouldBe("Latin1_General_100_CI_AI_SC");
        textOverflow.IsNullable.ShouldBeTrue();

        var isMin = table.Columns.Single(c => c.Name == "IsMin");
        isMin.SqlType.ShouldBe("bit");
        isMin.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenRealTokenSearchParamDdl_WhenParsed_ThenCodeColumnMatchesHandVerifiedCatalog()
    {
        // Arrange -- copied verbatim from 97.sql lines 883-890
        var ddl = """
            CREATE TABLE dbo.TokenSearchParam (
                ResourceTypeId      SMALLINT      NOT NULL,
                ResourceSurrogateId BIGINT        NOT NULL,
                SearchParamId       SMALLINT      NOT NULL,
                SystemId            INT           NULL,
                Code                VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
                CodeOverflow        VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam", StringComparison.Ordinal));
        var table = tables[0];

        // Assert -- matches SqlCatalogTests.GivenTokenSearchParam_WhenLookedUp_ThenCodeColumnMatchesRealDdl
        var code = table.Columns.Single(c => c.Name == "Code");
        code.SqlType.ShouldBe("varchar");
        code.MaxLength.ShouldBe(256);
        code.Collation.ShouldBe("Latin1_General_100_CS_AS");
        code.IsNullable.ShouldBeFalse();

        var systemId = table.Columns.Single(c => c.Name == "SystemId");
        systemId.SqlType.ShouldBe("int");
        systemId.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenRealTokenQuantityCompositeSearchParamDdl_WhenParsed_ThenDecimalColumnsParseCorrectly()
    {
        // Arrange -- copied verbatim from 97.sql lines 848-860
        var ddl = """
            CREATE TABLE dbo.TokenQuantityCompositeSearchParam (
                ResourceTypeId      SMALLINT         NOT NULL,
                ResourceSurrogateId BIGINT           NOT NULL,
                SearchParamId       SMALLINT         NOT NULL,
                SystemId1           INT              NULL,
                Code1               VARCHAR (256)    COLLATE Latin1_General_100_CS_AS NOT NULL,
                SystemId2           INT              NULL,
                QuantityCodeId2     INT              NULL,
                SingleValue2        DECIMAL (36, 18) NULL,
                LowValue2           DECIMAL (36, 18) NULL,
                HighValue2          DECIMAL (36, 18) NULL,
                CodeOverflow1       VARCHAR (MAX)    COLLATE Latin1_General_100_CS_AS NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam", StringComparison.Ordinal));
        var table = tables[0];

        // Assert
        table.Columns.Count.ShouldBe(11);
        var singleValue2 = table.Columns.Single(c => c.Name == "SingleValue2");
        singleValue2.SqlType.ShouldBe("decimal");
        singleValue2.MaxLength.ShouldBe(36);
        singleValue2.IsNullable.ShouldBeTrue();

        var codeOverflow1 = table.Columns.Single(c => c.Name == "CodeOverflow1");
        codeOverflow1.SqlType.ShouldBe("varchar");
        codeOverflow1.MaxLength.ShouldBeNull();
        codeOverflow1.Collation.ShouldBe("Latin1_General_100_CS_AS");
        codeOverflow1.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenRealReferenceSearchParamDdl_WhenParsed_ThenReferenceResourceIdColumnMatchesHandVerifiedCatalog()
    {
        // Arrange -- copied verbatim from 97.sql lines 518-526
        var ddl = """
            CREATE TABLE dbo.ReferenceSearchParam (
                ResourceTypeId           SMALLINT      NOT NULL,
                ResourceSurrogateId      BIGINT        NOT NULL,
                SearchParamId            SMALLINT      NOT NULL,
                BaseUri                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
                ReferenceResourceTypeId  SMALLINT      NULL,
                ReferenceResourceId      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
                ReferenceResourceVersion INT           NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam", StringComparison.Ordinal));
        var table = tables[0];

        // Assert -- matches SqlCatalogTests.GivenReferenceSearchParam_WhenLookedUp_ThenReferenceResourceIdColumnMatchesRealDdl
        var referenceResourceId = table.Columns.Single(c => c.Name == "ReferenceResourceId");
        referenceResourceId.SqlType.ShouldBe("varchar");
        referenceResourceId.MaxLength.ShouldBe(64);
        referenceResourceId.Collation.ShouldBe("Latin1_General_100_CS_AS");
        referenceResourceId.IsNullable.ShouldBeFalse();

        var baseUri = table.Columns.Single(c => c.Name == "BaseUri");
        baseUri.MaxLength.ShouldBe(128);
        baseUri.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenRealResourceTypeDdl_WhenParsed_ThenNameColumnMatchesHandVerifiedCatalog()
    {
        // Arrange -- copied verbatim from 97.sql lines 681-686 (includes IDENTITY column and
        // table-level UNIQUE/PRIMARY KEY CLUSTERED constraints that must be skipped, not parsed as columns)
        var ddl = """
            CREATE TABLE dbo.ResourceType (
                ResourceTypeId SMALLINT      IDENTITY (1, 1) NOT NULL,
                Name           NVARCHAR (50) COLLATE Latin1_General_100_CS_AS NOT NULL,
                CONSTRAINT UQ_ResourceType_ResourceTypeId UNIQUE (ResourceTypeId),
                CONSTRAINT PKC_ResourceType PRIMARY KEY CLUSTERED (Name) WITH (DATA_COMPRESSION = PAGE)
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name == "ResourceType");
        var table = tables[0];

        // Assert -- matches SqlCatalogTests.GivenResourceType_WhenLookedUp_ThenNameColumnMatchesRealDdl
        table.Columns.Count.ShouldBe(2);
        var name = table.Columns.Single(c => c.Name == "Name");
        name.SqlType.ShouldBe("nvarchar");
        name.MaxLength.ShouldBe(50);
        name.Collation.ShouldBe("Latin1_General_100_CS_AS");
        name.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenRealSearchParamDdl_WhenParsed_ThenUriAndStatusColumnsMatchHandVerifiedCatalog()
    {
        // Arrange -- copied verbatim from 97.sql lines 703-711
        var ddl = """
            CREATE TABLE dbo.SearchParam (
                SearchParamId        SMALLINT           IDENTITY (1, 1) NOT NULL,
                Uri                  VARCHAR (128)      COLLATE Latin1_General_100_CS_AS NOT NULL,
                Status               VARCHAR (20)       NOT NULL,
                LastUpdated          DATETIMEOFFSET (7) NOT NULL,
                IsPartiallySupported BIT                NOT NULL,
                CONSTRAINT UQ_SearchParam_SearchParamId UNIQUE (SearchParamId),
                CONSTRAINT PKC_SearchParam PRIMARY KEY CLUSTERED (Uri) WITH (DATA_COMPRESSION = PAGE)
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name == "SearchParam");
        var table = tables[0];

        // Assert -- matches SqlCatalogTests.GivenSearchParam_WhenLookedUp_ThenUriAndStatusColumnsMatchRealDdl
        table.Columns.Count.ShouldBe(5);
        var uri = table.Columns.Single(c => c.Name == "Uri");
        uri.SqlType.ShouldBe("varchar");
        uri.MaxLength.ShouldBe(128);
        uri.Collation.ShouldBe("Latin1_General_100_CS_AS");
        uri.IsNullable.ShouldBeFalse();

        var status = table.Columns.Single(c => c.Name == "Status");
        status.SqlType.ShouldBe("varchar");
        status.MaxLength.ShouldBe(20);
        status.Collation.ShouldBeNull();
        status.IsNullable.ShouldBeFalse();
    }
}
