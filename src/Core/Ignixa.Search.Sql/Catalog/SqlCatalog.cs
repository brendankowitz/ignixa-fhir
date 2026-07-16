namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// Describes the tables and columns this compiler emits SQL against. Table/column facts (SqlCatalog.g.cs)
/// are generated from the real DDL (src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql)
/// by Ignixa.Search.Sql.Generators -- this file owns only lookup behavior, not data. Deliberately does not
/// describe storage convention (e.g. which column an overflowing string lands in) -- that is Lower's job,
/// encoded as a rule, not a catalog fact.
/// </summary>
public sealed partial class SqlCatalog
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

    public static SqlCatalog Default { get; } = new SqlCatalog(BuildFromDdl());

    private static partial IReadOnlyDictionary<string, TableDescriptor> BuildFromDdl();
}
