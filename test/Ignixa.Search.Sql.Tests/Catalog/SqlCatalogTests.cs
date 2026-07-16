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
}
