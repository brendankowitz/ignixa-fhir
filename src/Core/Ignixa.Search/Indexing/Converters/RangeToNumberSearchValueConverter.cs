// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Range"/> to a list of <see cref="NumberSearchValue"/>.
/// </summary>
public class RangeToNumberSearchValueConverter : FhirElementToSearchValueConverter<NumberSearchValue>
{
    public RangeToNumberSearchValueConverter()
        : base("Range")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        // low and high are Quantities, so the bound is their value child, not their own (always null) value.
        decimal? lowValue = (decimal?)value.Child("low").Scalar("value");
        decimal? highValue = (decimal?)value.Child("high").Scalar("value");

        if (lowValue.HasValue || highValue.HasValue) yield return new NumberSearchValue(lowValue, highValue);
    }
}
