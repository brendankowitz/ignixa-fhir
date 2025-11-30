// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Id"/> to a list of <see cref="TokenSearchValue"/>.
/// </summary>
public class IdToTokenSearchValueConverter : FhirElementToSearchValueConverter<TokenSearchValue>
{
    public IdToTokenSearchValueConverter()
        : base("id")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        string stringValue = value?.Value as string;

        if (string.IsNullOrWhiteSpace(stringValue)) yield break;

        yield return new TokenSearchValue(null, stringValue, null);
    }
}
