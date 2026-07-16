namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// Describes one search-index table's schema, as the real DDL in
/// src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql states it.
/// </summary>
public sealed record TableDescriptor(
    string SchemaName,
    string TableName,
    IReadOnlyList<ColumnDescriptor> Columns)
{
    public ColumnDescriptor Column(string name)
        => Columns.FirstOrDefault(c => c.Name == name)
           ?? throw new KeyNotFoundException($"Table {SchemaName}.{TableName} has no column named '{name}'.");
}
