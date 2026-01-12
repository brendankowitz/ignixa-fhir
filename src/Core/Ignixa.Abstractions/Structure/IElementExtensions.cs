// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Extension methods for <see cref="IElement"/> to improve navigation ergonomics.
/// </summary>
public static class IElementExtensions
{
    /// <summary>
    /// Returns the first child element with the specified name, or null if not found.
    /// </summary>
    /// <param name="element">The parent element.</param>
    /// <param name="name">The child element name.</param>
    /// <returns>The first child element if found; otherwise null.</returns>
    public static IElement? FirstChild(this IElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        var children = element.Children(name);
        return children.Count > 0 ? children[0] : null;
    }

    /// <summary>
    /// Gets the first child element with the specified name.
    /// </summary>
    /// <param name="element">The parent element.</param>
    /// <param name="name">The child element name.</param>
    /// <param name="child">The first child element if found.</param>
    /// <returns>True if a child was found; otherwise false.</returns>
    public static bool TryGetFirstChild(this IElement element, string name, out IElement? child)
    {
        child = element.FirstChild(name);
        return child is not null;
    }
}
