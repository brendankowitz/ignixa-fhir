// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Period"/> to a list of <see cref="DateTimeSearchValue"/>.
/// </summary>
public class PeriodToDateTimeSearchValueConverter : FhirElementToSearchValueConverter<DateTimeSearchValue>
{
    public PeriodToDateTimeSearchValueConverter()
        : base("Period")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        string startString = value.Scalar("start")?.ToString();
        string endString = value.Scalar("end")?.ToString();

        PartialDateTime start = string.IsNullOrWhiteSpace(startString) ? PartialDateTime.MinValue : PartialDateTime.Parse(startString);

        PartialDateTime end = string.IsNullOrWhiteSpace(endString) ? PartialDateTime.MaxValue : PartialDateTime.Parse(endString);

        yield return new DateTimeSearchValue(start, end);
    }
}
