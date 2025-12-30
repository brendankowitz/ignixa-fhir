// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using FhirPathExtensions = Ignixa.FhirPath.Evaluation.TypedElementExtensions;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Identifier"/> to a list of <see cref="TokenSearchValue"/>.
/// </summary>
public class IdentifierToTokenSearchValueConverter : FhirElementToSearchValueConverter<TokenSearchValue>
{
    public IdentifierToTokenSearchValueConverter()
        : base("Identifier")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        // Simple property access (immediate children) - use local Scalar
        string? stringValue = value.Scalar("value") as string;
        string? system = value.Scalar("system") as string;

        if (string.IsNullOrEmpty(stringValue)) yield break;

        // FHIRPath expressions for nested paths - must use FhirPathExtensions.Scalar
        // The local ElementExtensions.Scalar only accesses immediate children by name
        string? type = FhirPathExtensions.Scalar(value, "type.text") as string;

        // Extract identifier type information for :of-type search modifier
        // Identifier.type.coding[0].system and Identifier.type.coding[0].code
        string? identifierTypeSystem = FhirPathExtensions.Scalar(value, "type.coding.first().system") as string;
        string? identifierTypeCode = FhirPathExtensions.Scalar(value, "type.coding.first().code") as string;

        // Based on spec: http://hl7.org/fhir/search.html#token,
        // the text for identifier is specified by Identifier.type.text.
        yield return new TokenSearchValue(system, stringValue, type, identifierTypeSystem, identifierTypeCode);
    }
}
