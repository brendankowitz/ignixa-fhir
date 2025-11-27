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

    public static IEnumerable<string> AsStringValues(this IEnumerable<IElement> elements)
    {
        if (elements == null) return Enumerable.Empty<string>();

        return elements.Select(x => x.Value as string).Where(x => !string.IsNullOrWhiteSpace(x));
    }
}
