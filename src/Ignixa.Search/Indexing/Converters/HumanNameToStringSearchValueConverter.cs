// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="HumanName"/> to a list of <see cref="StringSearchValue"/>.
/// </summary>
public class HumanNameToStringSearchValueConverter : FhirTypedElementToSearchValueConverter<StringSearchValue>
{
    public HumanNameToStringSearchValueConverter()
        : base("HumanName")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
    {
        IEnumerable<ITypedElement> givenNames = value.Select("given");
        IEnumerable<ITypedElement> prefixes = value.Select("prefix");
        IEnumerable<ITypedElement> suffixes = value.Select("suffix");
        string family = value.Scalar("family") as string;
        string text = value.Scalar("text") as string;

        // https://www.hl7.org/fhir/patient.html recommends the following:
        // A server defined search that may match any of the string fields in the HumanName, including family, give, prefix, suffix, suffix, and/or text
        // we will do a basic search based on family or given or prefix or suffix or text for now. Details on localization will be handled later.
        foreach (string given in givenNames.AsStringValues()) yield return new StringSearchValue(given);

        if (!string.IsNullOrWhiteSpace(family)) yield return new StringSearchValue(family);

        foreach (string prefix in prefixes.AsStringValues()) yield return new StringSearchValue(prefix);

        foreach (string suffix in suffixes.AsStringValues()) yield return new StringSearchValue(suffix);

        if (!string.IsNullOrWhiteSpace(text)) yield return new StringSearchValue(text);
    }
}
