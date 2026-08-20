// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

public class ReferenceToTokenSearchValueConverter : FhirElementToSearchValueConverter<TokenSearchValue>
{
    private readonly IdentifierToTokenSearchValueConverter _identifierConverter = new();

    public ReferenceToTokenSearchValueConverter()
        : base("Reference")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        IReadOnlyList<IElement> identifiers = value.Children("identifier");
        IElement identifier = identifiers.Count == 0 ? null : identifiers[0];

        return identifier is null
            ? []
            : _identifierConverter.ConvertTo(identifier);
    }
}
