// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Range"/> to a list of <see cref="QuantitySearchValue"/>.
/// </summary>
public class RangeToQuantitySearchValueConverter : FhirElementToSearchValueConverter<QuantitySearchValue>
{
    public RangeToQuantitySearchValueConverter()
        : base("Range")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        IElement highValue = value.Child("high");
        IElement lowValue = value.Child("low");
        decimal? low = (decimal?)lowValue.Scalar("value");
        decimal? high = (decimal?)highValue.Scalar("value");

        if (!low.HasValue && !high.HasValue) return [];

        // A bound without a value says nothing about the range; the representative must be a bound that has one.
        IElement quantityRepresentativeValue = low.HasValue ? lowValue : highValue;

        // FROM https://www.hl7.org/fhir/datatypes.html#Range: "The unit and code/system elements of the low or high elements SHALL match."
        return QuantityUnitSearchValues.Create(
            quantityRepresentativeValue.Scalar("system") as string,
            quantityRepresentativeValue.Scalar("code") as string,
            quantityRepresentativeValue.Scalar("unit") as string,
            low,
            high);
    }
}
