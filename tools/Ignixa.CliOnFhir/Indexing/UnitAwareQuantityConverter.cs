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
/// Indexes Quantity like Ignixa's stock converter, plus an extra entry carrying <c>Quantity.unit</c> as the code when
/// it differs, so a search code matches either <c>Quantity.code</c> or <c>Quantity.unit</c>
/// (<c>value-quantity=185||lbs</c> matches code <c>[lb_av]</c>, unit <c>lbs</c>). <see cref="QuantitySearchValue"/>
/// has no unit field to match against after extraction. Kept local to the tool: changing the shared converter would
/// add quantity rows to the SQL backend's index.
/// </summary>
internal sealed class UnitAwareQuantityConverter : FhirElementToSearchValueConverter<QuantitySearchValue>
{
    public UnitAwareQuantityConverter()
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
