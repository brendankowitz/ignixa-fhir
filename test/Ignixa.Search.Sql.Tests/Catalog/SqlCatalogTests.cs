using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Tests.Catalog;

public class SqlCatalogTests
{
    [Fact]
    public void GivenStringSearchParam_WhenLookedUp_ThenTextColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("StringSearchParam");
        var text = table.Column("Text");
        var textOverflow = table.Column("TextOverflow");

        // Assert
        text.SqlType.ShouldBe("nvarchar");
        text.MaxLength.ShouldBe(256);
        text.Collation.ShouldBe("Latin1_General_100_CI_AI_SC");
        text.IsNullable.ShouldBeFalse();
        textOverflow.SqlType.ShouldBe("nvarchar");
        textOverflow.MaxLength.ShouldBeNull();
        textOverflow.Collation.ShouldBe("Latin1_General_100_CI_AI_SC");
        textOverflow.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenTokenSearchParam_WhenLookedUp_ThenCodeColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("TokenSearchParam");
        var code = table.Column("Code");
        var systemId = table.Column("SystemId");

        // Assert
        code.SqlType.ShouldBe("varchar");
        code.MaxLength.ShouldBe(256);
        code.Collation.ShouldBe("Latin1_General_100_CS_AS");
        code.IsNullable.ShouldBeFalse();
        systemId.SqlType.ShouldBe("int");
        systemId.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenReferenceSearchParam_WhenLookedUp_ThenReferenceResourceIdColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("ReferenceSearchParam");
        var referenceResourceId = table.Column("ReferenceResourceId");
        var baseUri = table.Column("BaseUri");

        // Assert
        referenceResourceId.SqlType.ShouldBe("varchar");
        referenceResourceId.MaxLength.ShouldBe(64);
        referenceResourceId.Collation.ShouldBe("Latin1_General_100_CS_AS");
        referenceResourceId.IsNullable.ShouldBeFalse();
        baseUri.MaxLength.ShouldBe(128);
        baseUri.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenResourceType_WhenLookedUp_ThenNameColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("ResourceType");
        var name = table.Column("Name");

        // Assert
        name.SqlType.ShouldBe("nvarchar");
        name.MaxLength.ShouldBe(50);
        name.Collation.ShouldBe("Latin1_General_100_CS_AS");
        name.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenSearchParam_WhenLookedUp_ThenUriAndStatusColumnsMatchRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("SearchParam");
        var uri = table.Column("Uri");
        var status = table.Column("Status");

        // Assert
        uri.SqlType.ShouldBe("varchar");
        uri.MaxLength.ShouldBe(128);
        uri.Collation.ShouldBe("Latin1_General_100_CS_AS");
        uri.IsNullable.ShouldBeFalse();
        status.SqlType.ShouldBe("varchar");
        status.MaxLength.ShouldBe(20);
        status.Collation.ShouldBeNull();
        status.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenAnUnknownTable_WhenLookedUp_ThenThrows()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act & Assert
        Should.Throw<KeyNotFoundException>(() => catalog.Table("NotARealTable"));
    }

    [Fact]
    public void GivenAKnownTable_WhenLookingUpAnUnknownColumn_ThenThrows()
    {
        // Arrange
        var catalog = SqlCatalog.Default;
        var table = catalog.Table("StringSearchParam");

        // Act & Assert
        Should.Throw<KeyNotFoundException>(() => table.Column("NotARealColumn"));
    }

    [Fact]
    public void GivenDateTimeSearchParam_WhenLookedUp_ThenStartDateTimeColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("DateTimeSearchParam");
        var column = table.Column("StartDateTime");

        // Assert
        column.SqlType.ShouldBe("datetime2");
        column.MaxLength.ShouldBe(7);
        column.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenNumberSearchParam_WhenLookedUp_ThenLowValueColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("NumberSearchParam");
        var column = table.Column("LowValue");

        // Assert
        column.SqlType.ShouldBe("decimal");
        column.MaxLength.ShouldBe(36);
        column.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenQuantitySearchParam_WhenLookedUp_ThenSystemIdColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("QuantitySearchParam");
        var column = table.Column("SystemId");

        // Assert
        column.SqlType.ShouldBe("int");
        column.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenUriSearchParam_WhenLookedUp_ThenUriColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("UriSearchParam");
        var column = table.Column("Uri");

        // Assert
        column.SqlType.ShouldBe("varchar");
        column.MaxLength.ShouldBe(256);
        column.Collation.ShouldBe("Latin1_General_100_CS_AS");
        column.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenTokenTokenCompositeSearchParam_WhenLookedUp_ThenCode1ColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("TokenTokenCompositeSearchParam");
        var column = table.Column("Code1");

        // Assert
        column.SqlType.ShouldBe("varchar");
        column.MaxLength.ShouldBe(256);
        column.Collation.ShouldBe("Latin1_General_100_CS_AS");
        column.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenTokenQuantityCompositeSearchParam_WhenLookedUp_ThenLowValue2ColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("TokenQuantityCompositeSearchParam");
        var column = table.Column("LowValue2");

        // Assert
        column.SqlType.ShouldBe("decimal");
        column.MaxLength.ShouldBe(36);
        column.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenTokenStringCompositeSearchParam_WhenLookedUp_ThenText2ColumnCollationMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("TokenStringCompositeSearchParam");
        var column = table.Column("Text2");

        // Assert
        column.SqlType.ShouldBe("nvarchar");
        column.MaxLength.ShouldBe(256);
        column.Collation.ShouldBe("Latin1_General_CI_AI");
        column.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenTokenDateTimeCompositeSearchParam_WhenLookedUp_ThenStartDateTime2ColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("TokenDateTimeCompositeSearchParam");
        var column = table.Column("StartDateTime2");

        // Assert
        column.SqlType.ShouldBe("datetime2");
        column.MaxLength.ShouldBe(7);
        column.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenTokenNumberNumberCompositeSearchParam_WhenLookedUp_ThenHasRangeColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("TokenNumberNumberCompositeSearchParam");
        var column = table.Column("HasRange");

        // Assert
        column.SqlType.ShouldBe("bit");
        column.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenReferenceTokenCompositeSearchParam_WhenLookedUp_ThenReferenceResourceId1ColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("ReferenceTokenCompositeSearchParam");
        var column = table.Column("ReferenceResourceId1");

        // Assert
        column.SqlType.ShouldBe("varchar");
        column.MaxLength.ShouldBe(64);
        column.Collation.ShouldBe("Latin1_General_100_CS_AS");
        column.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenTheResourceTable_WhenLookedUp_ThenHasResourceTypeIdAndResourceIdAndResourceSurrogateIdColumns()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("Resource");

        // Assert
        table.TableName.ShouldBe("Resource");
        table.Column("ResourceTypeId").ShouldNotBeNull();
        table.Column("ResourceId").ShouldNotBeNull();
        table.Column("ResourceSurrogateId").ShouldNotBeNull();
        table.Column("IsHistory").ShouldNotBeNull();
        table.Column("IsDeleted").ShouldNotBeNull();
    }
}
