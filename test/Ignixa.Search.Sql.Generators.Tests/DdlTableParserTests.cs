using Ignixa.Search.Sql.Generators;

namespace Ignixa.Search.Sql.Generators.Tests;

public class DdlTableParserTests
{
    [Fact]
    public void GivenASimpleTable_WhenParsed_ThenReturnsItsColumns()
    {
        // Arrange
        var ddl = """
            CREATE TABLE dbo.SimpleSearchParam (
                ResourceTypeId SMALLINT NOT NULL,
                Text NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
                TextOverflow NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam", StringComparison.Ordinal));

        // Assert
        tables.Count.ShouldBe(1);
        var table = tables[0];
        table.TableName.ShouldBe("SimpleSearchParam");
        table.Columns.Count.ShouldBe(3);
        table.Columns[0].Name.ShouldBe("ResourceTypeId");
        table.Columns[0].SqlType.ShouldBe("smallint");
        table.Columns[0].MaxLength.ShouldBeNull();
        table.Columns[0].IsNullable.ShouldBeFalse();
        table.Columns[1].Name.ShouldBe("Text");
        table.Columns[1].SqlType.ShouldBe("nvarchar");
        table.Columns[1].MaxLength.ShouldBe(256);
        table.Columns[1].Collation.ShouldBe("Latin1_General_100_CI_AI_SC");
        table.Columns[2].Name.ShouldBe("TextOverflow");
        table.Columns[2].MaxLength.ShouldBeNull(); // MAX -- not a numeric width
        table.Columns[2].IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenAColumnWithAConstraintDefault_WhenParsed_ThenTheConstraintIsIgnoredNotTreatedAsAColumn()
    {
        // Arrange
        var ddl = """
            CREATE TABLE dbo.FlagSearchParam (
                IsMin BIT CONSTRAINT flag_IsMin_Constraint DEFAULT 0 NOT NULL,
                IsMax BIT CONSTRAINT flag_IsMax_Constraint DEFAULT 0 NOT NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam", StringComparison.Ordinal));

        // Assert
        tables[0].Columns.Count.ShouldBe(2);
        tables[0].Columns[0].Name.ShouldBe("IsMin");
        tables[0].Columns[0].SqlType.ShouldBe("bit");
    }

    [Fact]
    public void GivenAMultiArgDecimalType_WhenParsed_ThenTheCommaInsideParensIsNotTreatedAsAColumnSeparator()
    {
        // Arrange
        var ddl = """
            CREATE TABLE dbo.NumberSearchParam (
                SingleValue DECIMAL (36, 18) NULL,
                LowValue DECIMAL (36, 18) NOT NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam", StringComparison.Ordinal));

        // Assert
        tables[0].Columns.Count.ShouldBe(2);
        tables[0].Columns[0].Name.ShouldBe("SingleValue");
        tables[0].Columns[0].SqlType.ShouldBe("decimal");
        tables[0].Columns[0].MaxLength.ShouldBe(36); // first numeric arg -- precision, not a string-width concept here
        tables[0].Columns[0].IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenATableNameThatDoesNotMatchTheFilter_WhenParsed_ThenItIsExcluded()
    {
        // Arrange
        var ddl = """
            CREATE TABLE dbo.EventLog (
                EventId BIGINT IDENTITY (1, 1) NOT NULL
            );
            CREATE TABLE dbo.StringSearchParam (
                ResourceTypeId SMALLINT NOT NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name.EndsWith("SearchParam", StringComparison.Ordinal));

        // Assert
        tables.Count.ShouldBe(1);
        tables[0].TableName.ShouldBe("StringSearchParam");
    }

    [Fact]
    public void GivenAnIdentityColumn_WhenParsed_ThenIdentityIsIgnoredLikeAnyOtherModifier()
    {
        // Arrange -- ResourceType/SearchParam lookup tables have IDENTITY primary key columns;
        // ColumnDescriptor has no IDENTITY concept, so the parser must tolerate and ignore it.
        var ddl = """
            CREATE TABLE dbo.ResourceType (
                ResourceTypeId SMALLINT IDENTITY (1, 1) NOT NULL,
                Name NVARCHAR (50) COLLATE Latin1_General_100_CS_AS NOT NULL
            );
            """;

        // Act
        var tables = DdlTableParser.ParseTables(ddl, name => name == "ResourceType");

        // Assert
        tables[0].Columns[0].Name.ShouldBe("ResourceTypeId");
        tables[0].Columns[0].SqlType.ShouldBe("smallint");
    }
}
