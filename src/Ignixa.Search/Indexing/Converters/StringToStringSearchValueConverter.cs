// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Search.Indexing.Converters;

public class StringToStringSearchValueConverter : FhirTypedElementToSearchValueConverter<StringSearchValue>
{
    public StringToStringSearchValueConverter()
        : base("string", "System.String")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
    {
        if (value?.Value is string stringValue) yield return new StringSearchValue(stringValue);
    }
}
