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
        var highValue = value.Scalar("high") as IElement;
        var lowValue = value.Scalar("low") as IElement;

        IElement quantityRepresentativeValue = lowValue ?? highValue;

        if (quantityRepresentativeValue != null)
        {
            string system = quantityRepresentativeValue.Scalar("system") as string;
            string code = quantityRepresentativeValue.Scalar("code") as string;

            // FROM https://www.hl7.org/fhir/datatypes.html#Range: "The unit and code/system elements of the low or high elements SHALL match."
            yield return new QuantitySearchValue(system, code, (decimal?)lowValue?.Scalar("value"), (decimal?)highValue?.Scalar("value"));
        }
    }
}
