/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath tree navigation function implementations.
 * Implements children(), descendants().
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Tree navigation function implementations for FhirPath expressions.
/// </summary>
internal static class TreeNavigationFunctions
{
    /// <summary>
    /// children() - Returns all immediate children of the focus elements.
    /// </summary>
    [FhirPathFunction("children",
        SupportedContexts = "any-any",
        ReturnType = "any",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TreeNavigation",
        Description = "Returns all immediate children of the focus elements")]
    public static IEnumerable<IElement> Children(IEnumerable<IElement> focus)
    {
        foreach (var element in focus)
        {
            foreach (var child in element.Children())
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// descendants() - Returns all descendants of the focus elements (recursive).
    /// </summary>
    [FhirPathFunction("descendants",
        SupportedContexts = "any-any",
        ReturnType = "any",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TreeNavigation",
        Description = "Returns all descendants of the focus elements recursively")]
    public static IEnumerable<IElement> Descendants(IEnumerable<IElement> focus)
    {
        List<IElement> result = [];
        var queue = new Queue<IElement>(focus);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in current.Children())
            {
                result.Add(child);
                queue.Enqueue(child);
            }
        }

        return result;
    }

    /// <summary>
    /// hasValue() - Returns true if the element has a primitive value.
    /// Per FHIR spec: returns true for elements with a Value property (primitives), false otherwise.
    /// </summary>
    [FhirPathFunction("hasValue",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TreeNavigation",
        Description = "Returns true if the element has a primitive value")]
    public static IEnumerable<IElement> HasValue(IEnumerable<IElement> focus)
    {
        var focusList = focus.ToList();

        if (focusList.Count == 0)
        {
            return [FunctionHelpers.CreateBoolean(false)];
        }

        bool hasValue = focusList.Any(e => e.Value is not null);
        return [FunctionHelpers.CreateBoolean(hasValue)];
    }
}
