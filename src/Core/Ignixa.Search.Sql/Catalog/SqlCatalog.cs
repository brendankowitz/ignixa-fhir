namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// Describes the tables and columns this compiler emits SQL against, as the real DDL states them.
/// Deliberately does not describe storage convention (e.g. which column an overflowing string
/// lands in) -- that is Lower's job, encoded as a rule, not a catalog fact. See
/// docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md.
/// </summary>
public sealed class SqlCatalog
{
    private readonly IReadOnlyDictionary<string, TableDescriptor> _tables;

    private SqlCatalog(IReadOnlyDictionary<string, TableDescriptor> tables)
    {
        _tables = tables;
    }

    public TableDescriptor Table(string name)
        => _tables.TryGetValue(name, out var table)
           ? table
           : throw new KeyNotFoundException($"SqlCatalog has no table named '{name}'.");

    /// <summary>
    /// The catalog for this phase's known tables. Populated from Task 2 Step 1's real DDL read
    /// (src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql) -- intentionally covers
    /// only StringSearchParam/TokenSearchParam/ReferenceSearchParam plus the ResourceType/SearchParam
    /// lookup tables; the remaining 10 leaf types are Phase 5's job.
    /// </summary>
    public static SqlCatalog Default { get; } = Build();

    private static SqlCatalog Build()
    {
        // dbo.StringSearchParam (Resources/97.sql lines 713-721)
        var stringSearchParam = new TableDescriptor("dbo", "StringSearchParam",
        [
            new ColumnDescriptor("ResourceTypeId", "smallint", null, null, false),
            new ColumnDescriptor("ResourceSurrogateId", "bigint", null, null, false),
            new ColumnDescriptor("SearchParamId", "smallint", null, null, false),
            new ColumnDescriptor("Text", "nvarchar", 256, "Latin1_General_100_CI_AI_SC", false),
            new ColumnDescriptor("TextOverflow", "nvarchar", null, "Latin1_General_100_CI_AI_SC", true),
            new ColumnDescriptor("IsMin", "bit", null, null, false),
            new ColumnDescriptor("IsMax", "bit", null, null, false),
        ]);

        // dbo.TokenSearchParam (Resources/97.sql lines 883-890)
        // Note: Code/CodeOverflow are VARCHAR (not NVARCHAR) with a case-sensitive collation,
        // unlike StringSearchParam's case-insensitive Text/TextOverflow.
        var tokenSearchParam = new TableDescriptor("dbo", "TokenSearchParam",
        [
            new ColumnDescriptor("ResourceTypeId", "smallint", null, null, false),
            new ColumnDescriptor("ResourceSurrogateId", "bigint", null, null, false),
            new ColumnDescriptor("SearchParamId", "smallint", null, null, false),
            new ColumnDescriptor("SystemId", "int", null, null, true),
            new ColumnDescriptor("Code", "varchar", 256, "Latin1_General_100_CS_AS", false),
            new ColumnDescriptor("CodeOverflow", "varchar", null, "Latin1_General_100_CS_AS", true),
        ]);

        // dbo.ReferenceSearchParam (Resources/97.sql lines 518-526)
        var referenceSearchParam = new TableDescriptor("dbo", "ReferenceSearchParam",
        [
            new ColumnDescriptor("ResourceTypeId", "smallint", null, null, false),
            new ColumnDescriptor("ResourceSurrogateId", "bigint", null, null, false),
            new ColumnDescriptor("SearchParamId", "smallint", null, null, false),
            new ColumnDescriptor("BaseUri", "varchar", 128, "Latin1_General_100_CS_AS", true),
            new ColumnDescriptor("ReferenceResourceTypeId", "smallint", null, null, true),
            new ColumnDescriptor("ReferenceResourceId", "varchar", 64, "Latin1_General_100_CS_AS", false),
            new ColumnDescriptor("ReferenceResourceVersion", "int", null, null, true),
        ]);

        // dbo.ResourceType (Resources/97.sql lines 681-686)
        var resourceType = new TableDescriptor("dbo", "ResourceType",
        [
            new ColumnDescriptor("ResourceTypeId", "smallint", null, null, false),
            new ColumnDescriptor("Name", "nvarchar", 50, "Latin1_General_100_CS_AS", false),
        ]);

        // dbo.SearchParam (Resources/97.sql lines 703-711)
        // Note: Status has no explicit COLLATE clause in the DDL -- it inherits the database
        // default collation rather than a specific named one, hence Collation: null (not a guess).
        var searchParam = new TableDescriptor("dbo", "SearchParam",
        [
            new ColumnDescriptor("SearchParamId", "smallint", null, null, false),
            new ColumnDescriptor("Uri", "varchar", 128, "Latin1_General_100_CS_AS", false),
            new ColumnDescriptor("Status", "varchar", 20, null, false),
            new ColumnDescriptor("LastUpdated", "datetimeoffset", null, null, false),
            new ColumnDescriptor("IsPartiallySupported", "bit", null, null, false),
        ]);

        var tables = new Dictionary<string, TableDescriptor>
        {
            [stringSearchParam.TableName] = stringSearchParam,
            [tokenSearchParam.TableName] = tokenSearchParam,
            [referenceSearchParam.TableName] = referenceSearchParam,
            [resourceType.TableName] = resourceType,
            [searchParam.TableName] = searchParam,
        };

        return new SqlCatalog(tables);
    }
}
