/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * ViewConstant model for SQL on FHIR v2.
 * Represents a constant that can be referenced in FHIRPath expressions.
 */

namespace Ignixa.SqlOnFhir.Models;

/// <summary>
/// Represents a constant value that can be referenced in FHIRPath expressions using %constant_name syntax.
/// Constants allow parameterization of ViewDefinitions.
/// </summary>
public class ViewConstant
{
    /// <summary>
    /// Name of the constant (referenced as %name in FHIRPath expressions).
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The constant value. Extracted from the value[Type] property in the JSON.
    /// The parser will extract the value from properties like valueString, valueInteger, valueBoolean, etc.
    /// and store it here as a typed object (string, int, bool, decimal, DateTime, etc.).
    /// </summary>
    public object? Value { get; set; }
}
