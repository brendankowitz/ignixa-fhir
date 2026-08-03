namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// One column's schema-derived facts — name, SQL type, length, collation, nullability — as the DDL states
/// them. <c>MaxLength</c> is the DDL's first parenthesized type argument (VARCHAR(256) → 256,
/// DECIMAL(36,18) → 36, DATETIME2(7) → 7), covering width, precision, and fractional-seconds uniformly.
/// </summary>
public sealed record ColumnDescriptor(
    string Name,
    string SqlType,
    int? MaxLength,
    string? Collation,
    bool IsNullable);
