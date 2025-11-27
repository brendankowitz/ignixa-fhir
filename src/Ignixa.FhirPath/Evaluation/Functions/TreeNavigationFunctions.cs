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
}
