// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

public class AddressToStringSearchValueConverter : FhirElementToSearchValueConverter<StringSearchValue>
{
    public AddressToStringSearchValueConverter()
        : base("Address")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        // http://hl7.org/fhir/patient.html recommends the following:
        // A server defined search that may match any of the string fields in the Address, including line, city, state, country, postalCode, and/or text.
        // we will do a basic search based on any of the address component for now. Details on localization will be handled later.

        string city = value.Scalar("city") as string;
        string country = value.Scalar("country") as string;
        string district = value.Scalar("district") as string;
        IEnumerable<IElement> lines = value.Select("line");
        string postCode = value.Scalar("postalCode") as string;
        string state = value.Scalar("state") as string;
        string text = value.Scalar("text") as string;

        if (!string.IsNullOrWhiteSpace(city)) yield return new StringSearchValue(city);

        if (!string.IsNullOrWhiteSpace(country)) yield return new StringSearchValue(country);

        if (!string.IsNullOrWhiteSpace(district)) yield return new StringSearchValue(district);

        foreach (string line in lines.AsStringValues()) yield return new StringSearchValue(line);

        if (!string.IsNullOrWhiteSpace(postCode)) yield return new StringSearchValue(postCode);

        if (!string.IsNullOrWhiteSpace(state)) yield return new StringSearchValue(state);

        if (!string.IsNullOrWhiteSpace(text)) yield return new StringSearchValue(text);
    }
}
