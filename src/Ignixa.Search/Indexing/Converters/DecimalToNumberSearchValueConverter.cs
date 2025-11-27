// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="FhirDecimal"/> to a list of <see cref="NumberSearchValue"/>.
/// </summary>
public class DecimalToNumberSearchValueConverter : FhirElementToSearchValueConverter<NumberSearchValue>
{
    public DecimalToNumberSearchValueConverter()
        : base("decimal", "System.Decimal")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        if (value?.Value == null) yield break;

        yield return new NumberSearchValue((decimal)value.Value);
    }
}
