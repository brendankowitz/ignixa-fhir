// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

internal static class TypedElementExtensions
{
    public static TokenSearchValue ToTokenSearchValue(this ITypedElement coding)
    {
        EnsureArg.IsNotNull(coding, nameof(coding));

        string system = coding.Scalar("system") as string;
        string code = coding.Scalar("code") as string;
        string display = coding.Scalar("display") as string;

        if (!string.IsNullOrWhiteSpace(system) ||
            !string.IsNullOrWhiteSpace(code) ||
            !string.IsNullOrWhiteSpace(display))
            return new TokenSearchValue(system, code, display);

        return null;
    }

    public static IEnumerable<string> AsStringValues(this IEnumerable<ITypedElement> elements)
    {
        if (elements == null) return Enumerable.Empty<string>();

        return elements.Select(x => x.Value as string).Where(x => !string.IsNullOrWhiteSpace(x));
    }
}
