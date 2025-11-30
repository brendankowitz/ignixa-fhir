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
        decimal? lowValue = (decimal?)value.Scalar("low");
        decimal? highValue = (decimal?)value.Scalar("high");

        if (lowValue.HasValue || highValue.HasValue) yield return new NumberSearchValue(lowValue, highValue);
    }
}
