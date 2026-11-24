// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Types;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// A unit converter for one evaluation: charges the UCUM work it delegates to its evaluation's budget, and answers
/// a question it has already answered in this evaluation from a memo for one step.
/// </summary>
/// <remarks>
/// <para>
/// UCUM's cost per call depends on which symbols a unit contains, not on anything the evaluator can count - a
/// single derived symbol such as <c>H</c> costs milliseconds - so the delegated call is metered by the clock
/// (<see cref="EvaluationBudget.ChargeElapsed"/>). That is the only part of a budget charge that varies between
/// runs; the memo is per evaluation, so which calls reach UCUM does not.
/// </para>
/// <para>
/// Answers are memoised exactly as the inner converter gave them. A conversion is keyed on its value's scale as
/// well as its value, because <c>1.0</c> and <c>1.00</c> are equal but convert to results of different scale,
/// which equivalence (<c>~</c>) reads as precision. Each memo holds at most <see cref="MemoCapacity"/> answers;
/// beyond that a question is still answered and charged, just not remembered.
/// </para>
/// </remarks>
internal sealed class MeteredUnitConverter(EvaluationBudget budget, IQuantityUnitConverter inner) : IQuantityUnitConverter
{
    private const int MemoCapacity = 4096;

    private readonly Dictionary<(string, string), bool> _compatible = [];
    private readonly Dictionary<(decimal, byte, string, string), decimal?> _converted = [];
    private readonly Dictionary<string, string?> _dimensions = new(StringComparer.Ordinal);
    private readonly Func<(string, string), bool> _isCompatible = key => inner.IsCompatible(key.Item1, key.Item2);
    private readonly Func<(decimal, byte, string, string), decimal?> _convert = key => inner.Convert(key.Item1, key.Item3, key.Item4);
    private readonly Func<string, string?> _getDimensionality = inner.GetDimensionality;

    public bool IsCompatible(string unit1, string unit2)
    {
        ArgumentNullException.ThrowIfNull(unit1);
        ArgumentNullException.ThrowIfNull(unit2);

        return Memoised(_compatible, (unit1, unit2), _isCompatible);
    }

    public decimal? Convert(decimal value, string fromUnit, string toUnit)
    {
        ArgumentNullException.ThrowIfNull(fromUnit);
        ArgumentNullException.ThrowIfNull(toUnit);

        return Memoised(_converted, (value, value.Scale, fromUnit, toUnit), _convert);
    }

    public string? GetDimensionality(string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return Memoised(_dimensions, unit, _getDimensionality);
    }

    public FhirQuantity? Multiply(FhirQuantity left, FhirQuantity right)
    {
        var start = Stopwatch.GetTimestamp();
        var product = inner.Multiply(left, right);
        budget.ChargeElapsed(start);
        return product;
    }

    public FhirQuantity? Divide(FhirQuantity left, FhirQuantity right)
    {
        var start = Stopwatch.GetTimestamp();
        var quotient = inner.Divide(left, right);
        budget.ChargeElapsed(start);
        return quotient;
    }

    private TValue Memoised<TKey, TValue>(Dictionary<TKey, TValue> memo, TKey key, Func<TKey, TValue> compute)
        where TKey : notnull
    {
        if (memo.TryGetValue(key, out var known))
        {
            budget.Step();
            return known;
        }

        var start = Stopwatch.GetTimestamp();
        var answer = compute(key);
        budget.ChargeElapsed(start);

        if (memo.Count < MemoCapacity)
        {
            memo[key] = answer;
        }

        return answer;
    }
}
