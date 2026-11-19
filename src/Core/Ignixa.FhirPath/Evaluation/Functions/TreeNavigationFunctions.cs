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
    public static IEnumerable<IElement> Children(IEnumerable<IElement> focus, EvaluationContext context)
    {
        var budget = context.Budget;
        foreach (var element in focus)
        {
            budget.Step();
            foreach (var child in element.Children())
            {
                budget.Step();
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
    /// <remarks>
    /// Eager, so every node is charged a step and counted against
    /// <see cref="FhirPathEvaluationLimits.MaxCollectionSize"/> as it is appended: the input can be a collection the
    /// expression multiplied (<c>%resource</c> combined with itself), whose descendants are that many copies of the
    /// whole tree.
    /// </remarks>
    public static IEnumerable<IElement> Descendants(IEnumerable<IElement> focus, EvaluationContext context)
    {
        var budget = context.Budget;
        List<IElement> result = [];
        var queue = new Queue<IElement>(focus);

        while (queue.Count > 0)
        {
            // A step per node visited, leaf or not: a focus of many primitives has no children to charge for.
            budget.Step();
            var current = queue.Dequeue();
            foreach (var child in current.Children())
            {
                budget.Step();
                result.Add(child);
                budget.EnsureCollectionSize(result.Count, "descendants()");
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
    public static IEnumerable<IElement> HasValue(IEnumerable<IElement> focus, EvaluationContext context)
    {
        var focusList = context.Budget.PerItem(focus).ToList();

        if (focusList.Count == 0)
        {
            return [FunctionHelpers.CreateBoolean(false)];
        }

        bool hasValue = focusList.Any(e => e.Value is not null);
        return [FunctionHelpers.CreateBoolean(hasValue)];
    }
}
