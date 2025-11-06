/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * ViewDefinition model for SQL on FHIR v2.
 * Defines how to flatten FHIR resources into tabular rows.
 * Based on: https://sql-on-fhir.org/
 */

namespace Ignixa.SqlOnFhir.Models;

/// <summary>
/// Represents a ViewDefinition resource that maps FHIR resources to tabular columns.
/// Used for SQL on FHIR v2 to define how resources should be flattened into rows.
/// Conforms to the official SQL on FHIR v2 specification.
/// </summary>
public class ViewDefinition
{
    /// <summary>
    /// FHIR resource type this ViewDefinition applies to (e.g., "Patient").
    /// </summary>
    public required string Resource { get; set; }

    /// <summary>
    /// Optional status for filtering. If specified, only processes resources with matching status.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Constants that can be referenced in FHIRPath expressions using %constant_name syntax.
    /// Allows parameterization of ViewDefinitions.
    /// </summary>
#pragma warning disable CA2227 // Collection properties should be read-only
    public IList<ViewConstant>? Constant { get; set; }

    /// <summary>
    /// WHERE clauses for filtering resources before column extraction.
    /// Each clause contains a FHIRPath expression that must evaluate to true.
    /// </summary>
    public IList<WhereClause>? Where { get; set; }

    /// <summary>
    /// SELECT groups defining columns to extract from the resource.
    /// Each SELECT group can have its own forEach/forEachOrNull for array unnesting.
    /// Multiple SELECT groups create separate row groups with shared base columns.
    /// </summary>
    public required IList<SelectGroup> Select { get; set; } = [];
#pragma warning restore CA2227 // Collection properties should be read-only
}
