namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// The tables and columns the compiler emits SQL against. The table/column facts are source-generated
/// from the schema DDL by Ignixa.Search.Sql.Generators; this file owns only the lookup behavior, not the
/// data. It describes the schema, not storage convention (e.g. which column an overflowing string lands
/// in) — that is a Lower rule, not a catalog fact.
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
