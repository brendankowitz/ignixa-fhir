// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.SourceNodeSerialization.Abstractions;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="CodeableConcept"/> to a list of <see cref="TokenSearchValue"/>.
/// </summary>
public class CodeableConceptToTokenSearchValueConverter : FhirTypedElementToSearchValueConverter<TokenSearchValue>
{
    public CodeableConceptToTokenSearchValueConverter()
        : base("CodeableConcept")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
    {
        // Based on spec: http://hl7.org/fhir/search.html#token,
        // CodeableConcept.text is searchable, but we will only create a dedicated entry for it
        // if it is different from the display text of one of its codings

        string text = value.Scalar("text") as string;
        bool conceptTextNeedsToBeAdded = !string.IsNullOrWhiteSpace(text);

        foreach (ITypedElement coding in value.Select("coding"))
        {
            var searchValue = coding?.ToTokenSearchValue();

            if (searchValue != null)
            {
                if (conceptTextNeedsToBeAdded) conceptTextNeedsToBeAdded = !string.Equals(text, searchValue.Text, StringComparison.OrdinalIgnoreCase);

                yield return searchValue;
            }
        }

        if (conceptTextNeedsToBeAdded) yield return new TokenSearchValue(null, null, text);
    }
}
