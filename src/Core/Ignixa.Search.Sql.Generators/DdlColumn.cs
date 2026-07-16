namespace Ignixa.Search.Sql.Generators;

public sealed class DdlColumn
{
    public DdlColumn(string name, string sqlType, int? maxLength, string? collation, bool isNullable)
    {
        Name = name;
        SqlType = sqlType;
        MaxLength = maxLength;
        Collation = collation;
        IsNullable = isNullable;
    }

    public string Name { get; }
    public string SqlType { get; }
    public int? MaxLength { get; }
    public string? Collation { get; }
    public bool IsNullable { get; }
}
