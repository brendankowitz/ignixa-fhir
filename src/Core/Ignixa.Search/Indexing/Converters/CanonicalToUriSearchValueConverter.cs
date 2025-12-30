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
         * When separateCanonicalComponents=true, the URI is parsed to extract:
         * - Base URI (without version/fragment) stored in Uri column
         * - Version (after |) stored in Version column
         * - Fragment (after #) stored in Fragment column
         * This enables :below modifier search on canonical URIs.
         */

        yield return new UriSearchValue(value.Value.ToString(), separateCanonicalComponents: true);
    }
}
