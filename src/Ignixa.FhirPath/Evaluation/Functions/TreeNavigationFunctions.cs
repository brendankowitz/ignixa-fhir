/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath tree navigation function implementations.
 * Implements children(), descendants().
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Tree navigation function implementations for FhirPath expressions.
/// </summary>
internal static class TreeNavigationFunctions
{
    /// <summary>
    /// children() - Returns all immediate children of the focus elements.
    /// </summary>
    public static IEnumerable<ITypedElement> Children(IEnumerable<ITypedElement> focus)
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
    public static IEnumerable<ITypedElement> Descendants(IEnumerable<ITypedElement> focus)
    {
        var result = new List<ITypedElement>();
        var queue = new Queue<ITypedElement>(focus);

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
}
