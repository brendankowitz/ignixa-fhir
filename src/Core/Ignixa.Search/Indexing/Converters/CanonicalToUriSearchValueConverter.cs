// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Canonical"/> to a list of <see cref="UriSearchValue"/>.
/// </summary>
public class CanonicalToUriSearchValueConverter : FhirElementToSearchValueConverter<UriSearchValue>
{
    public CanonicalToUriSearchValueConverter()
        : base("canonical")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        if (value?.Value == null) yield break;

        /* For more information see: https://www.hl7.org/fhir/search.html#uri
         *
         * "Note that for uri parameters that refer to the Canonical URLs of the conformance and knowledge resources
         * (e.g. StructureDefinition, ValueSet, PlanDefinition etc), servers SHOULD support searching by canonical references,
         * and SHOULD support automatically detecting a |[version] portion as part of the search parameter, and interpreting that
         * portion as a search on the version"
         *
         * Note: Using separateCanonicalComponents=false to store the full URI in the Uri column.
         * Full canonical version/fragment search requires schema migration to add separate columns.
         * Until then, exact matching on the full URI is supported.
         */

        yield return new UriSearchValue(value.Value.ToString(), false);
    }
}
