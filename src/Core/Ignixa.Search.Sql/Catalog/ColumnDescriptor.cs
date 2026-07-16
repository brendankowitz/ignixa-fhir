namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// Describes one column's schema-derived facts -- name, SQL type, length, collation, nullability --
/// as the DDL states them. Does not describe storage convention (see
/// docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md, "Lower owns storage convention").
/// </summary>
public sealed record ColumnDescriptor(
    string Name,
    string SqlType,
    int? MaxLength,
    string? Collation,
    bool IsNullable);
