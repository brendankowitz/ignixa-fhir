// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Oid"/> to a list of <see cref="UriSearchValue"/>.
/// </summary>
public class OidToUriSearchValueConverter : FhirElementToSearchValueConverter<UriSearchValue>
{
    public OidToUriSearchValueConverter()
        : base("oid")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        if (value?.Value == null) yield break;

        yield return new UriSearchValue(value.Value.ToString(), false);
    }
}
