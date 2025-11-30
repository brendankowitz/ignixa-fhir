// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

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
        string stringValue = value.Scalar("value") as string;
        string system = value.Scalar("system") as string;
        string type = value.Scalar("type.text") as string;

        if (string.IsNullOrEmpty(stringValue)) yield break;

        // Based on spec: http://hl7.org/fhir/search.html#token,
        // the text for identifier is specified by Identifier.type.text.
        yield return new TokenSearchValue(system, stringValue, type);
    }
}
