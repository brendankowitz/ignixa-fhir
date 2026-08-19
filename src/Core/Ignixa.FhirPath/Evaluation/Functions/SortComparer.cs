/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The ordering rule behind sort(), min() and max(), kept tri-state so indeterminacy stays distinguishable
 * from equality.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Types;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Orders two FHIRPath values for <c>sort()</c>, <c>min()</c> and <c>max()</c>.
/// </summary>
/// <remarks>
/// <para>
/// This replaces two near-identical comparers that differed only in where a null key sorted, and that
/// both dispatched on the non-generic <see cref="IComparable"/>. <see cref="FhirTemporal"/> deliberately
/// does not implement it - see the <c>CA1036</c> suppression on that type - and
/// <see cref="FhirQuantity"/> implements no ordering at all, so both fell through to an ordinal compare
/// of <c>ToString()</c>. That sorted <c>10 'mg'</c> before <c>9 'mg'</c> and separated two spellings of
/// the same instant. Both comparers also wrapped <c>CompareTo</c> in a bare <c>catch</c> that answered
/// "equal", so a genuine type mismatch silently interleaved unrelated values.
/// </para>
/// <para>
/// The comparison itself is <see cref="CompareValues"/> and is tri-state: <see langword="null"/> means
/// the ordering is indeterminate, which is a different answer from zero. Only <see cref="Compare"/>
/// collapses the two, and only because <see cref="IComparer{T}"/> has nowhere else to go.
/// </para>
/// </remarks>
internal sealed class SortComparer : IComparer<IElement?>
{
    private static readonly IQuantityUnitConverter UnitConverter = QuantityUnitConverter.Instance;

    private readonly bool _nullsHigh;

    private SortComparer(bool nullsHigh)
    {
        _nullsHigh = nullsHigh;
    }

    /// <summary>
    /// Gets the comparer that treats a missing key as less than any value.
    /// </summary>
    public static SortComparer NullsLow { get; } = new(nullsHigh: false);

    /// <summary>
    /// Gets the comparer that treats a missing key as greater than any value, so that a descending sort
    /// - which negates this comparer's result - places missing keys first.
    /// </summary>
    public static SortComparer NullsHigh { get; } = new(nullsHigh: true);

    /// <summary>
    /// Orders two sort keys, resolving an indeterminate comparison to "do not reorder".
    /// </summary>
    /// <param name="x">The left key, or <see langword="null"/> when the key expression yielded nothing.</param>
    /// <param name="y">The right key, or <see langword="null"/> when the key expression yielded nothing.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    /// <exception cref="FhirPathEvaluationException">The two keys have no ordering defined between them.</exception>
    public int Compare(IElement? x, IElement? y)
    {
        const string Function = "sort()";

        var left = x?.Value;
        var right = y?.Value;

        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return _nullsHigh ? 1 : -1;
        }

        if (right is null)
        {
            return _nullsHigh ? -1 : 1;
        }

        // Collapsing indeterminate to zero is an interpretation, not a spec requirement. sort() is not in
        // normative FHIRPath 2.0.0 at all; the 3.0.0 build says only that "attempting to sort items with
        // incompatible types will result in an error" and is silent on what a pairwise comparison that is
        // *empty* rather than erroneous should do. Zero is chosen because OrderBy/ThenBy are stable, so it
        // means "leave these two in the order they arrived" rather than "these are equal".
        //
        // There is no Firely behaviour to match here, and an earlier revision of this comment wrongly said
        // there was. Firely 5.11.4 - the version the fhir-server seam must reproduce - does not implement
        // sort() at all: a scan of Hl7.Fhir.Base.dll 5.11.4 finds no registration for sort, avg, sum or max,
        // and no ValueProviderComparer, runSort or OrderedNode type. Those exist only on Firely's later
        // development line. So this rule is answerable to the spec text above and to nothing else, and no
        // expression reaching the seam can currently exercise it through the Firely provider.
        //
        // The coalesce lives here rather than inside CompareValues so the comparison stays honest about
        // which pairs it could not order.
        return CompareValues(x!, y!, Function) ?? 0;
    }

    /// <summary>
    /// Compares two values using FHIRPath ordering semantics.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="function">The FHIRPath function requesting the comparison, for diagnostics.</param>
    /// <returns>
    /// A negative value, zero, a positive value, or <see langword="null"/> when the ordering is
    /// indeterminate - incompatible quantity units, or temporals whose precision or timezone presence
    /// makes them overlap rather than order. Both of those are the spec's own empty results.
    /// </returns>
    /// <exception cref="FhirPathEvaluationException">
    /// The operands are of types no conversion relates, or of a type FHIRPath defines no ordering for.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Neither operand's <see cref="IElement.Value"/> may be <see langword="null"/>; callers screen that
    /// case, because a missing key is a question about where nulls sort rather than about ordering.
    /// </para>
    /// <para>
    /// <c>min()</c> and <c>max()</c> call this too. The FHIRPath spec defines them in terms of the
    /// <c>&lt;</c> and <c>&gt;</c> operators - <c>aggregate(iif($total.empty(), $this, iif($this &lt; $total,
    /// $this, $total)))</c> - so they must not have an ordering rule of their own, which is exactly the
    /// mistake they used to make: a per-type ladder chosen from the collection's first element, with units
    /// matched by raw string equality and temporals re-parsed to <see cref="DateTime"/>.
    /// <paramref name="function"/> exists only so the resulting error names the caller.
    /// </para>
    /// </remarks>
    public static int? CompareValues(IElement left, IElement right, string function)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftValue = left.Value!;
        var rightValue = right.Value!;

        if (TemporalOperand.IsTemporal(leftValue, left.InstanceType)
            || TemporalOperand.IsTemporal(rightValue, right.InstanceType))
        {
            return CompareTemporals(left, right, function);
        }

        // FHIRPath's Comparison section defines the ordering operators for String, Integer, Decimal,
        // Quantity, Date, DateTime and Time only. Boolean is not among them, and it reaches here as an
        // IComparable that would happily order false before true, so it has to be excluded by name.
        if (leftValue is bool || rightValue is bool)
        {
            throw NotOrderable(left, right, function);
        }

        if (leftValue is FhirQuantity || rightValue is FhirQuantity)
        {
            return CompareQuantities(left, right, function);
        }

        if (TryCompareNumbers(leftValue, rightValue, out var numeric))
        {
            return numeric;
        }

        if (leftValue is string leftText && rightValue is string rightText)
        {
            return string.Compare(leftText, rightText, StringComparison.Ordinal);
        }

        // The non-generic IComparable is safe once the runtime types are known to match, which is the guard
        // the old comparers lacked: it is precisely the cross-type CompareTo that threw and got swallowed.
        // Every value this engine puts in IElement.Value that implements IComparable<T> also implements the
        // non-generic form, apart from FhirTemporal and FhirQuantity - and both are handled above.
        if (leftValue.GetType() == rightValue.GetType() && leftValue is IComparable comparable)
        {
            return comparable.CompareTo(rightValue);
        }

        throw NotOrderable(left, right, function);
    }

    /// <summary>
    /// Orders two temporals, reconciling a typed <see cref="FhirTemporal"/> against the raw string a
    /// FHIRPath <c>@</c>-literal still evaluates to.
    /// </summary>
    private static int? CompareTemporals(IElement left, IElement right, string function)
    {
        var leftTemporal = TemporalOperand.AsTemporal(left.Value, left.InstanceType);
        var rightTemporal = TemporalOperand.AsTemporal(right.Value, right.InstanceType);

        if (leftTemporal is null || rightTemporal is null)
        {
            // One side is a temporal and the other is not a value any temporal reading reaches. A
            // malformed literal lands here too, and is indeterminate rather than an error: unparseable
            // wire data is an expected input, not an ill-formed expression.
            return TemporalOperand.IsTemporal(left.Value, left.InstanceType)
                && TemporalOperand.IsTemporal(right.Value, right.InstanceType)
                    ? null
                    : throw NotOrderable(left, right, function);
        }

        return FhirTemporal.Compare(leftTemporal, rightTemporal);
    }

    /// <summary>
    /// Orders two quantities by converting the right operand into the left's unit.
    /// </summary>
    private static int? CompareQuantities(IElement left, IElement right, string function)
    {
        var leftQuantity = AsQuantity(left.Value!);
        var rightQuantity = AsQuantity(right.Value!);

        if (leftQuantity is null || rightQuantity is null)
        {
            throw NotOrderable(left, right, function);
        }

        if (!UnitConverter.IsCompatible(leftQuantity.Unit, rightQuantity.Unit))
        {
            return null;
        }

        var converted = rightQuantity.ConvertTo(leftQuantity.Unit, UnitConverter);

        return converted is null ? null : leftQuantity.Value.CompareTo(converted.Value);
    }

    /// <summary>
    /// Reads a value as a quantity, applying FHIRPath's implicit Integer/Decimal to Quantity conversion.
    /// </summary>
    /// <remarks>
    /// The unity unit is what makes <c>1 'mg'</c> against <c>5</c> an incompatible-units case rather than
    /// a type error, which is the same reading <see cref="QuantityEvaluator.EvaluateComparison"/> gives
    /// the <c>&lt;</c> and <c>&gt;</c> operators. Sorting, comparing and totalling must not disagree
    /// about it, which is why <see cref="AggregateFunctions"/> reads its operands through here too.
    /// </remarks>
    internal static FhirQuantity? AsQuantity(object value)
    {
        return value switch
        {
            FhirQuantity quantity => quantity,
            _ => TryToDecimal(value, out var number) ? new FhirQuantity(number, "1") : null
        };
    }

    /// <summary>
    /// Compares two numbers across the integer and decimal types, so that <c>1</c> and <c>1.0</c> order
    /// by value rather than by CLR type.
    /// </summary>
    /// <remarks>
    /// A <see cref="double"/> operand demotes the whole comparison to binary floating point rather than
    /// widening to <see cref="decimal"/>, because the decimal range does not cover the double range and
    /// the conversion would overflow. FHIRPath's own numeric type is Decimal; a double only reaches
    /// <see cref="IElement.Value"/> by way of a JSON reader, so the precision loss is bounded to values
    /// that were already approximate.
    /// </remarks>
    private static bool TryCompareNumbers(object left, object right, out int result)
    {
        if (left is double or float || right is double or float)
        {
            if (TryToDouble(left, out var leftDouble) && TryToDouble(right, out var rightDouble))
            {
                result = leftDouble.CompareTo(rightDouble);
                return true;
            }

            result = 0;
            return false;
        }

        if (TryToDecimal(left, out var leftDecimal) && TryToDecimal(right, out var rightDecimal))
        {
            result = leftDecimal.CompareTo(rightDecimal);
            return true;
        }

        result = 0;
        return false;
    }

    /// <summary>
    /// Reads a value as a <see cref="decimal"/> across FHIRPath's Integer and Decimal types.
    /// </summary>
    /// <remarks>
    /// <see cref="AggregateFunctions"/> totals through this so that "which CLR types are a FHIRPath
    /// number" has one answer. Note what is absent: <see cref="double"/> and <see cref="float"/> are
    /// deliberately not here, because ordering demotes to binary floating point rather than widening -
    /// see <see cref="TryCompareNumbers"/> - and a total, which has no such escape, bridges them itself.
    /// </remarks>
    internal static bool TryToDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            default:
                result = 0m;
                return false;
        }
    }

    private static bool TryToDouble(object value, out double result)
    {
        switch (value)
        {
            case double doubleValue:
                result = doubleValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case decimal decimalValue:
                result = (double)decimalValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            default:
                result = 0d;
                return false;
        }
    }

    private static FhirPathEvaluationException NotOrderable(IElement left, IElement right, string function)
    {
        return new FhirPathEvaluationException(
            $"{function} cannot order operands of type '{FhirPathEvaluator.DescribeOperandType(left)}' " +
            $"and '{FhirPathEvaluator.DescribeOperandType(right)}'.");
    }
}
