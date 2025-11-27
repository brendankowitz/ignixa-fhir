/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath boolean function implementations.
 * Implements allTrue(), anyTrue(), allFalse(), anyFalse(), not().
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Boolean function implementations for FhirPath expressions.
/// </summary>
internal static class BooleanFunctions
{
    /// <summary>
    /// allTrue() - Returns true if all elements are boolean true.
    /// Empty collection returns true.
    /// </summary>
    public static IEnumerable<IElement> AllTrue(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count == 0)
            return [FunctionHelpers.CreateBoolean(true)]; // Empty collection returns true

        var allTrue = list.All(e => e.Value is bool b && b);
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return [FunctionHelpers.CreateBoolean(allTrue)];
    }

    /// <summary>
    /// anyTrue() - Returns true if any element is boolean true.
    /// Empty collection returns false.
    /// </summary>
    public static IEnumerable<IElement> AnyTrue(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        // Empty collection means false (no true values found)
        if (list.Count == 0)
            return [FunctionHelpers.CreateBoolean(false)];

        var anyTrue = list.Any(e => e.Value is bool b && b);
        return [FunctionHelpers.CreateBoolean(anyTrue)];
    }

    /// <summary>
    /// allFalse() - Returns true if all elements are boolean false.
    /// Empty collection returns true.
    /// </summary>
    public static IEnumerable<IElement> AllFalse(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count == 0)
            return [FunctionHelpers.CreateBoolean(true)]; // Empty collection returns true

        var allFalse = list.All(e => e.Value is bool b && !b);
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return [FunctionHelpers.CreateBoolean(allFalse)];
    }

    /// <summary>
    /// anyFalse() - Returns true if any element is boolean false.
    /// Empty collection returns false.
    /// </summary>
    public static IEnumerable<IElement> AnyFalse(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        // Empty collection means false (no false values found)
        if (list.Count == 0)
            return [FunctionHelpers.CreateBoolean(false)];

        var anyFalse = list.Any(e => e.Value is bool b && !b);
        return [FunctionHelpers.CreateBoolean(anyFalse)];
    }

    /// <summary>
    /// not() - Negates a single boolean value.
    /// Returns empty if collection is empty or has more than one element.
    /// </summary>
    public static IEnumerable<IElement> Not(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();

        // Empty collection returns empty (per FHIRPath spec)
        if (list.Count == 0)
            return [];

        // Single boolean: negate it
        if (list.Count == 1 && list[0].Value is bool b)
        {
            // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
            return [FunctionHelpers.CreateBoolean(!b)];
        }

        // Multiple items or non-boolean: per spec, this is an error
        // Return empty for safety
        return [];
    }
}
