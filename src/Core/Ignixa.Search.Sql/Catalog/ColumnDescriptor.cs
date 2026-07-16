namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// Describes one column's schema-derived facts -- name, SQL type, length, collation, nullability --
/// as the DDL states them. Does not describe storage convention (see
/// docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md, "Lower owns storage convention").
///
/// <para>
/// MaxLength represents the DDL's first parenthesized type argument: VARCHAR(256) → MaxLength=256,
/// DECIMAL(36,18) → MaxLength=36, DATETIME2(7) → MaxLength=7. This covers character width,
/// numeric precision, and temporal fractional-seconds precision uniformly.
/// </para>
/// </summary>
public sealed record ColumnDescriptor(
    string Name,
    string SqlType,
    int? MaxLength,
    string? Collation,
    bool IsNullable);
