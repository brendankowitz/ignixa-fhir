// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// The quantity index values for one Quantity (or a Range's bounds): <c>system|code</c>, plus the stated human unit
/// when it differs from the code.
/// </summary>
/// <remarks>
/// The quantity search section (R4 §3.1.1.4.11, R5 §3.2.1.5.10, https://hl7.org/fhir/R5/search.html#quantity) says
/// <c>5.4||mg</c> matches "where the unit - either the code (code) or the stated human unit (unit) are 'mg'", while
/// <c>5.4|http://unitsofmeasure.org|mg</c> constrains system and code together. <see cref="QuantitySearchValue"/> has
/// no unit field and the SQL quantity rows have no unit column, so the unit is indexed as a second value whose code is
/// the unit and whose system is absent: a search without a system matches it through the ordinary code comparison
/// (in memory and in SQL), and a search with a system never does, because the second value has none. SQL still lowers
/// an empty system to <c>SystemId IS NULL</c>, so there a system-less search misses the coded value row; that predates
/// this converter and is tracked separately.
/// </remarks>
internal static class QuantityUnitSearchValues
{
    public static IReadOnlyList<ISearchValue> Create(string? system, string? code, string? unit, decimal? low, decimal? high)
    {
        var byCode = new QuantitySearchValue(system, code, low, high);

        return string.IsNullOrWhiteSpace(unit) || string.Equals(unit, code, StringComparison.Ordinal)
            ? [byCode]
            : [byCode, new QuantitySearchValue(null, unit, low, high)];
    }
}
