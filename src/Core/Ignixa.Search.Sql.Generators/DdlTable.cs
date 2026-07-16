using System.Collections.Generic;

namespace Ignixa.Search.Sql.Generators;

public sealed class DdlTable
{
    public DdlTable(string schemaName, string tableName, IReadOnlyList<DdlColumn> columns)
    {
        SchemaName = schemaName;
        TableName = tableName;
        Columns = columns;
    }

    public string SchemaName { get; }
    public string TableName { get; }
    public IReadOnlyList<DdlColumn> Columns { get; }
}
