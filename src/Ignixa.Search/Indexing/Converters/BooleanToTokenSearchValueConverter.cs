// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.SourceNodeSerialization.ElementModel;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="FhirBoolean"/> to a list of <see cref="TokenSearchValue"/>.
/// </summary>
public class BooleanToTokenSearchValueConverter : FhirTypedElementToSearchValueConverter<TokenSearchValue>
{
    public BooleanToTokenSearchValueConverter()
        : base("boolean", "System.Boolean")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
    {
        object fhirValue = value?.Value;

        if (fhirValue == null) yield break;

        yield return new TokenSearchValue("http://terminology.hl7.org/CodeSystem/special-values", (bool)fhirValue ? "true" : "false", null);
    }
}
