/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath boolean function implementations.
 * Implements allTrue(), anyTrue(), allFalse(), anyFalse(), not().
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;

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
    [FhirPathFunction("allTrue",
        SupportedContexts = "boolean-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Boolean",
        Description = "Returns true if all elements are boolean true")]
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
    [FhirPathFunction("anyTrue",
        SupportedContexts = "boolean-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Boolean",
        Description = "Returns true if any element is boolean true")]
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
    [FhirPathFunction("allFalse",
        SupportedContexts = "boolean-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Boolean",
        Description = "Returns true if all elements are boolean false")]
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
    [FhirPathFunction("anyFalse",
        SupportedContexts = "boolean-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Boolean",
        Description = "Returns true if any element is boolean false")]
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
    /// not() - Returns the negation of the boolean evaluation of the input.
    /// Returns empty if collection is empty.
    /// Per FHIRPath spec: single boolean returns !value, otherwise returns !true (any content is truthy).
    /// Multiple items is an invalid operation per spec (testNotInvalid), but we return empty for compatibility.
    /// </summary>
    [FhirPathFunction("not",
        SupportedContexts = "boolean-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Boolean",
        Description = "Negates a single boolean value")]
    public static IEnumerable<IElement> Not(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();

        // Empty collection returns empty (per FHIRPath spec)
        if (list.Count == 0)
            return [];

        // Multiple items: per FHIRPath spec this is an invalid operation (testNotInvalid)
        // We return empty for compatibility rather than throwing
        if (list.Count != 1)
            return [];

        // Single element: check if it's a boolean
        var element = list[0];
        if (element.Value is bool b)
        {
            return [FunctionHelpers.CreateBoolean(!b)];
        }

        // Per FHIRPath spec testIntegerBooleanNotTrue/False: single non-boolean element is "truthy" (it exists)
        // So not(truthy) = false
        return [FunctionHelpers.CreateBoolean(false)];
    }
}
