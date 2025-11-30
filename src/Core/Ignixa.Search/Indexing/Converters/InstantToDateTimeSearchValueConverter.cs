// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.Utilities;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Instant"/> to a list of <see cref="DateTimeSearchValue"/>.
/// </summary>
public class InstantToDateTimeSearchValueConverter : FhirElementToSearchValueConverter<DateTimeSearchValue>
{
    public InstantToDateTimeSearchValueConverter()
        : base("instant")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        string stringValue = value?.Value?.ToString();

        if (stringValue == null) yield break;

        DateTimeOffset val = PrimitiveTypeConverter.ConvertTo<DateTimeOffset>(stringValue);

        yield return new DateTimeSearchValue(val);
    }
}
