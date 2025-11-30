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
public class QuantityToQuantitySearchValueConverter : FhirElementToSearchValueConverter<QuantitySearchValue>
{
    public QuantityToQuantitySearchValueConverter()
        : base("Quantity", "System.Quantity")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        decimal? decimalValue = (decimal?)value.Scalar("value");

        if (!decimalValue.HasValue) yield break;

        string system = value.Scalar("system")?.ToString();
        string code = value.Scalar("code")?.ToString();

        yield return new QuantitySearchValue(
            system,
            code,
            decimalValue.GetValueOrDefault());
    }
}
