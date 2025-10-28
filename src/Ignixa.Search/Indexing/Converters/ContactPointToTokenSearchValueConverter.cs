// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="ContactPoint"/> to a list of <see cref="TokenSearchValue"/>.
/// </summary>
public class ContactPointToTokenSearchValueConverter : FhirTypedElementToSearchValueConverter<TokenSearchValue>
{
    public ContactPointToTokenSearchValueConverter()
        : base("ContactPoint")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
    {
        string stringValue = value.Scalar("value") as string;
        string use = value.Scalar("use") as string;

        if (string.IsNullOrWhiteSpace(stringValue)) yield break;

        yield return new TokenSearchValue(use, stringValue, null);
    }
}
