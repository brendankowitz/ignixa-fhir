/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * SelectGroup model for SQL on FHIR v2.
 * Defines a group of columns and optional array unnesting within a SELECT clause.
 */

namespace Ignixa.SqlOnFhir.Models;

/// <summary>
/// Represents a SELECT group within a ViewDefinition.
/// Each SELECT group defines a set of columns to extract and can optionally unnest an array.
/// Multiple SELECT groups create separate row groups (cartesian product with base columns).
/// </summary>
public class SelectGroup
{
    /// <summary>
    /// List of column definitions to extract from the resource.
    /// Each column specifies a FHIRPath expression and target SQL type.
    /// </summary>
#pragma warning disable CA2227 // Collection properties should be read-only
    public IList<ViewColumnDefinition>? Column { get; set; }
#pragma warning restore CA2227 // Collection properties should be read-only

    /// <summary>
    /// FHIRPath expression identifying an array to unnest.
    /// For each element in the array, columns are evaluated with the element as context.
    /// If the array is empty, the row is omitted.
    /// </summary>
    public string? ForEach { get; set; }

    /// <summary>
    /// FHIRPath expression identifying an array to unnest.
    /// For each element in the array, columns are evaluated with the element as context.
    /// If the array is empty, one row is created with null values for array-dependent columns.
    /// </summary>
    public string? ForEachOrNull { get; set; }

    /// <summary>
    /// List of FHIRPath expressions that define paths to recursively traverse.
    /// The view runner will recursively follow each path to any depth, collecting results
    /// from all levels. All results are combined using a union operation.
    /// Example: ["item", "answer.item"] will recursively collect all items at any nesting depth.
    /// </summary>
#pragma warning disable CA2227 // Collection properties should be read-only
    public IList<string>? Repeat { get; set; }
#pragma warning restore CA2227 // Collection properties should be read-only
}
