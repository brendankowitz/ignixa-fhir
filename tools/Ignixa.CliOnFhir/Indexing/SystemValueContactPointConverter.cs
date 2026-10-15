// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
// Copied from fhir-candle src/FhirCandle.Engine/Search/CandleSearchValueConverters.cs at 6d0af1e (MIT); adapted for cli-on-fhir.

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// Indexes ContactPoint as <c>ContactPoint.system|ContactPoint.value</c> (<c>telecom=phone|555-1234</c>). Ignixa's
/// stock converter uses <c>ContactPoint.use</c> ("home"/"work") as the token system instead. Kept local to the tool:
/// changing the shared converter would change the token rows the SQL backend stores.
/// </summary>
internal sealed class SystemValueContactPointConverter : FhirElementToSearchValueConverter<TokenSearchValue>
{
    public SystemValueContactPointConverter()
        : base("ContactPoint")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        if (value.Scalar("value") is not string stringValue || string.IsNullOrWhiteSpace(stringValue))
        {
            yield break;
        }

        yield return new TokenSearchValue(value.Scalar("system") as string, stringValue, null);
    }
}
