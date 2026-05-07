/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * WhereClause model for SQL on FHIR v2.
 * Defines filtering conditions for ViewDefinitions.
 */

namespace Ignixa.SqlOnFhir.Models;

/// <summary>
/// Represents a WHERE clause within a ViewDefinition.
/// WHERE clauses filter resources before column extraction by evaluating FHIRPath expressions.
/// All WHERE clauses must evaluate to true for a resource to be included.
/// </summary>
public class WhereClause
{
    /// <summary>
    /// FHIRPath expression to evaluate for filtering.
    /// The expression must evaluate to true (or a non-empty collection) for the resource to be included.
    /// Examples:
    /// - "active = true" - include only active resources
    /// - "name.where(use = 'official').exists()" - include only resources with official name
    /// - "birthDate > @1990-01-01" - include only resources with birthDate after 1990
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Human-readable description of what this WHERE clause filters.
    /// Metadata only — no evaluation impact.
    /// </summary>
    public string? Description { get; set; }
}
