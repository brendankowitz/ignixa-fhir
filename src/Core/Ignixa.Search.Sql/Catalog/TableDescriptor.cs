namespace Ignixa.Search.Sql.Catalog;

/// <summary>One search-index table's schema — its name and columns — as the DDL states it.</summary>
public sealed record TableDescriptor(
    string SchemaName,
    string TableName,
    IReadOnlyList<ColumnDescriptor> Columns)
{
    public ColumnDescriptor Column(string name)
        => Columns.FirstOrDefault(c => c.Name == name)
           ?? throw new KeyNotFoundException($"Table {SchemaName}.{TableName} has no column named '{name}'.");
}
