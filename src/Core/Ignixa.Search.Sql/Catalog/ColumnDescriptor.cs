namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// One column's schema-derived facts — name, SQL type, length, collation, nullability — as the DDL
/// states them.
/// <para>
/// MaxLength is the DDL's first parenthesized type argument: VARCHAR(256) → 256, DECIMAL(36,18) → 36,
/// DATETIME2(7) → 7. It covers character width, numeric precision, and temporal fractional-seconds
/// precision uniformly.
/// </para>
/// </summary>
public sealed record ColumnDescriptor(
    string Name,
    string SqlType,
    int? MaxLength,
    string? Collation,
    bool IsNullable);
