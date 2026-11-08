// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Quantity"/> to a list of <see cref="QuantitySearchValue"/>.
/// </summary>
/// <remarks>
/// A quantity whose stated human unit differs from its code is indexed twice: once as <c>system|code</c> and once with
/// the unit as the code and no system (see <see cref="QuantityUnitSearchValues"/>).
/// </remarks>
public class QuantityToQuantitySearchValueConverter : FhirElementToSearchValueConverter<QuantitySearchValue>
{
    public QuantityToQuantitySearchValueConverter()
        : base("Quantity", "System.Quantity")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        decimal? decimalValue = (decimal?)value.Scalar("value");

        if (!decimalValue.HasValue) return [];

        return QuantityUnitSearchValues.Create(
            value.Scalar("system")?.ToString(),
            value.Scalar("code")?.ToString(),
            value.Scalar("unit")?.ToString(),
            decimalValue,
            decimalValue);
    }
}
