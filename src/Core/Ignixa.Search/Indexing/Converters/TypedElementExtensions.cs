// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

internal static class ElementExtensions
{
    public static object Scalar(this IElement element, string name)
    {
        if (element == null) return null;
        var children = element.Children(name);
        return children.Count > 0 ? children[0].Value : null;
    }

    public static TokenSearchValue ToTokenSearchValue(this IElement coding)
    {
        EnsureArg.IsNotNull(coding, nameof(coding));

        string system = FhirPath.Evaluation.TypedElementExtensions.Scalar(coding, "system") as string;
        string code = FhirPath.Evaluation.TypedElementExtensions.Scalar(coding, "code") as string;
        string display = FhirPath.Evaluation.TypedElementExtensions.Scalar(coding, "display") as string;

        if (!string.IsNullOrWhiteSpace(system) ||
            !string.IsNullOrWhiteSpace(code) ||
            !string.IsNullOrWhiteSpace(display))
            return new TokenSearchValue(system, code, display);

        return null;
    }

    /// <summary>
    /// Projects elements to their wire lexical form, dropping those that have none.
    /// </summary>
    /// <remarks>
    /// <see cref="IElement.Value"/> returns a <see cref="FhirTemporal"/> for date, dateTime, instant and
    /// time primitives, so an <c>as string</c> cast would silently drop any temporal an expression
    /// happened to select. Routing through the engine's normalization chokepoint keeps indexing and
    /// evaluation agreeing on what a primitive's lexical form is. Values with no lexical form (booleans,
    /// numbers, complex types) still yield nothing, exactly as before.
    /// </remarks>
    public static IEnumerable<string> AsStringValues(this IEnumerable<IElement> elements)
    {
        if (elements == null) return Enumerable.Empty<string>();

        return elements
            .Select(x => FhirPath.Evaluation.WireValue.AsWireString(x.Value))
            .Where(x => !string.IsNullOrWhiteSpace(x));
    }
}
