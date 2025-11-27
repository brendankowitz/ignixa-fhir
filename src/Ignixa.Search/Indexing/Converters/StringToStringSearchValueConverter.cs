// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;

namespace Ignixa.Search.Indexing.Converters;

public class StringToStringSearchValueConverter : FhirElementToSearchValueConverter<StringSearchValue>
{
    public StringToStringSearchValueConverter()
        : base("string", "System.String")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        if (value?.Value is string stringValue) yield return new StringSearchValue(stringValue);
    }
}
