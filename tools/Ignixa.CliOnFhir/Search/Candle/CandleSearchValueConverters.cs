// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
// Copied from fhir-candle src/FhirCandle.Engine/Search/CandleSearchValueConverters.cs at 6d0af1e (MIT); adapted for cli-on-fhir.

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.Search.Candle;

/// <summary>
/// Indexes ContactPoint as <c>ContactPoint.system|ContactPoint.value</c>, matching the
/// pre-migration evaluator (<c>telecom=phone|...</c>). Ignixa's stock converter uses
/// <c>ContactPoint.use</c> ("home"/"work") as the token system instead.
/// </summary>
public sealed class CandleContactPointToTokenSearchValueConverter : FhirElementToSearchValueConverter<TokenSearchValue>
{
    public CandleContactPointToTokenSearchValueConverter()
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

/// <summary>
/// Indexes Quantity like Ignixa's stock converter, plus an extra entry carrying
/// <c>Quantity.unit</c> as the code when it differs - the pre-migration evaluator matched a
/// search code against both <c>Quantity.code</c> and <c>Quantity.unit</c>
/// (<c>value-quantity=185||lbs</c> matching code <c>[lb_av]</c>, unit <c>lbs</c>), and
/// <see cref="QuantitySearchValue"/> has no unit field to match against post-extraction.
/// </summary>
public sealed class CandleQuantityToQuantitySearchValueConverter : FhirElementToSearchValueConverter<QuantitySearchValue>
{
    public CandleQuantityToQuantitySearchValueConverter()
        : base("Quantity", "System.Quantity")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        if ((decimal?)value.Scalar("value") is not decimal decimalValue)
        {
            yield break;
        }

        string? system = value.Scalar("system")?.ToString();
        string? code = value.Scalar("code")?.ToString();
        string? unit = value.Scalar("unit")?.ToString();

        yield return new QuantitySearchValue(system, code, decimalValue);

        if (!string.IsNullOrEmpty(unit) && !string.Equals(unit, code, StringComparison.OrdinalIgnoreCase))
        {
            yield return new QuantitySearchValue(system, unit, decimalValue);
        }
    }
}
