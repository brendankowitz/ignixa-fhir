using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Tests.Catalog;

/// <summary>
/// The catalog carries the non-search tables the data layer hand-writes SQL against, so those queries can
/// source table and column names from the real DDL instead of string literals. The compiler never looks
/// these up; they exist for <c>Ignixa.DataLayer.SqlServer</c>, which already references this project.
/// <para>
/// Each fact below also pins a DDL construct the parser did not originally handle, because the search-index
/// tables never use it. Deleting these tests would let the parser silently regress on the exact shapes that
/// made these tables unparseable.
/// </para>
/// </summary>
public class SqlCatalogDataLayerTablesTests
{
    [Fact]
    public void GivenSystem_WhenLookedUp_ThenIdentityDeclaredBeforeNullabilityParses()
    {
        // dbo.System declares `SystemId INT IDENTITY (1, 1) NOT NULL` -- IDENTITY *before* the nullability
        // clause. PackageResource below declares the opposite order; both appear in this schema.
        var table = SqlCatalog.Default.Table("System");

        var systemId = table.Column("SystemId");
        var value = table.Column("Value");

        systemId.SqlType.ShouldBe("int");
        systemId.IsNullable.ShouldBeFalse();
        value.SqlType.ShouldBe("nvarchar");
        value.MaxLength.ShouldBe(256);
        value.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenPackageResource_WhenLookedUp_ThenIdentityDeclaredAfterNullabilityParses()
    {
        // `PackageResourceId BIGINT NOT NULL IDENTITY (1, 1)` -- IDENTITY *after* nullability, the opposite
        // of dbo.System's order above.
        var table = SqlCatalog.Default.Table("PackageResource");

        var id = table.Column("PackageResourceId");
        var version = table.Column("Version");

        id.SqlType.ShouldBe("bigint");
        id.IsNullable.ShouldBeFalse();

        // Version is the nullable one -- a real distinction the ports depend on, since "latest by
        // canonical" has to treat a missing version differently from a present one.
        version.SqlType.ShouldBe("nvarchar");
        version.MaxLength.ShouldBe(100);
        version.IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void GivenSourceEvents_WhenLookedUp_ThenMaxLengthAndDefaultClausesParse()
    {
        var table = SqlCatalog.Default.Table("SourceEvents");

        var eventData = table.Column("EventData");
        var timestamp = table.Column("Timestamp");

        // NVARCHAR (MAX) models as a null MaxLength, matching the existing convention for TextOverflow.
        eventData.SqlType.ShouldBe("nvarchar");
        eventData.MaxLength.ShouldBeNull();
        eventData.IsNullable.ShouldBeFalse();

        // `DATETIMEOFFSET DEFAULT sysutcdatetime() NOT NULL` -- a DEFAULT between type and nullability.
        timestamp.SqlType.ShouldBe("datetimeoffset");
        timestamp.IsNullable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("BackgroundJobs")]
    [InlineData("TermCodeSystem")]
    [InlineData("TermConcept")]
    [InlineData("TermValueSet")]
    [InlineData("TermConceptMap")]
    public void GivenADataLayerTable_WhenLookedUp_ThenItResolves(string tableName)
    {
        // Table() throws KeyNotFoundException on a miss, so resolving at all is the assertion. These are the
        // tables the SqlServer data-layer hand-writes SQL against.
        Should.NotThrow(() => SqlCatalog.Default.Table(tableName));
    }

    [Fact]
    public void GivenATableOutsideTheDeclaredSet_WhenLookedUp_ThenItStillThrows()
    {
        // The catalog is a named set, not the whole schema: the wider schema contains constructs the parser
        // does not model (EventLog's PERSISTED computed column). A miss must stay loud, so that adding a
        // table to the set is a deliberate act rather than something a caller discovers at runtime.
        Should.Throw<KeyNotFoundException>(() => SqlCatalog.Default.Table("EventLog"));
    }
}
