/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * ViewDefinition column definition model for SQL on FHIR v2.
 */

namespace Ignixa.SqlOnFhir.Models;

/// <summary>
/// Represents a column definition in a ViewDefinition.
/// Maps a FHIRPath expression to a tabular column with type conversion.
/// </summary>
public class ViewColumnDefinition
{
    /// <summary>
    /// Column name in the output table (e.g., "family_name", "birth_date").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// FHIRPath expression to extract the column value from a resource.
    /// Examples: "id", "name.where(use='official').first().family", "birthDate"
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Optional SQL type for this column.
    /// Values: "string", "integer", "decimal", "boolean", "date", "datetime"
    /// If not specified, type is inferred from the FHIRPath result.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Optional human-readable description of the column.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this column should NOT be null (for validation purposes).
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Whether this column aggregates multiple values as an array.
    /// When true, all matching values from the FHIRPath expression are collected into an array.
    /// When false (default), only the first value is used.
    /// </summary>
    public bool Collection { get; set; }
}
