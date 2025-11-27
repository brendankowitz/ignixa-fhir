// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Integer"/> to a list of <see cref="NumberSearchValue"/>.
/// </summary>
public class IntegerToNumberSearchValueConverter : FhirElementToSearchValueConverter<NumberSearchValue>
{
    public IntegerToNumberSearchValueConverter()
        : base("integer", "positiveInt", "unsignedInt", "System.Integer")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        if (value?.Value == null) yield break;

        yield return new NumberSearchValue((int)value.Value);
    }
}
