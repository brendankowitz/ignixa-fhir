// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="ContactPoint"/> to a list of <see cref="TokenSearchValue"/>.
/// </summary>
/// <remarks>
/// Indexes <c>ContactPoint.value</c> as the token code with no system. The token mapping table of the search page
/// (R4 §3.1.1.4.10, R5 §3.2.1.5.13, https://hl7.org/fhir/R5/search.html#token) maps ContactPoint to
/// <c>ContactPoint.value</c> alone, leaving the system column empty, and both versions state that "for token
/// parameters on elements of type ContactPoint, uri, or boolean, the presence of the pipe symbol SHALL NOT be used -
/// only the [parameter]=[code] form is allowed". Neither <c>ContactPoint.use</c> (home/work) nor
/// <c>ContactPoint.system</c> (phone/email) is therefore a token system: the spec selects by those with dedicated
/// parameters (<c>phone</c>, <c>email</c>) whose expressions filter on <c>system</c>.
/// </remarks>
public class ContactPointToTokenSearchValueConverter : FhirElementToSearchValueConverter<TokenSearchValue>
{
    public ContactPointToTokenSearchValueConverter()
        : base("ContactPoint")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        string stringValue = value.Scalar("value") as string;

        if (string.IsNullOrWhiteSpace(stringValue)) yield break;

        yield return new TokenSearchValue(null, stringValue, null);
    }
}
