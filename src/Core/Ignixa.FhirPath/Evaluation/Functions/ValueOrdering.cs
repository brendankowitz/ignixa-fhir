/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * How two FHIRPath values order, in the two forms callers need: tri-state for the operators min() and
 * max() are defined in terms of, and a total order for sort().
 */

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Types;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Orders two FHIRPath values, and supplies the type coercions the aggregate functions share with the
/// comparison operators.
/// </summary>
/// <remarks>
/// <para>
/// Two surfaces are needed and they answer different questions.
/// <see cref="CompareValues"/> is FHIRPath comparison: tri-state, where <see langword="null"/> means the
/// spec declines to decide - incompatible quantity units, overlapping partial-precision temporals - which
/// is a different answer from zero. <see cref="CompareForSort"/> is a total order for <c>sort()</c>,
/// where there is no third state to return.
/// </para>
/// <para>
/// <see cref="CompareForSort"/> is not <see cref="CompareValues"/> with the indeterminate case coalesced
/// to zero. That was the previous design and it produced an intransitive comparer: <c>@2012</c> is
/// indeterminate against both <c>@2012-01</c> and <c>@2012-06</c> while those two order determinately, so
/// "indeterminate means equal" made equality non-transitive and <c>IComparer&lt;T&gt;</c>'s contract was
/// violated. Three permutations of one multiset then sorted three different ways, one of them inverting
/// a determinately-ordered pair. The fix is to derive each value's position from the value alone - a
/// temporal's <see cref="FhirTemporal.CompareTo"/> key, a quantity's canonical magnitude within its
/// dimension - so transitivity holds by construction. Where FHIRPath is determinate the two surfaces
/// agree, because a determinate <c>A &lt; B</c> means A's interval ends before B's begins, which orders
/// their keys the same way; where FHIRPath is indeterminate the total order still has to answer, and
/// answers deterministically.
/// </para>
/// <para>
/// The <c>sort()</c> text does bear on this: "Items are considered equal if and only if the equals
/// (=) operator returns true. (i.e. false and empty both indicate that the items are not equal)."
/// Reporting an empty comparison as equal contradicts it directly. That text is from the FHIRPath
/// continuous build off <c>master</c> (<c>HL7/FHIRPath</c>, <c>input/pages/index.md</c>), checked
/// 2026-08-19; it is unreleased, and §sort() is marked <c>{:.stu}</c> throughout, so it can move.
/// </para>
/// </remarks>
internal static class ValueOrdering
{
    private static readonly IQuantityUnitConverter UnitConverter = QuantityUnitConverter.Instance;

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
    /// <c>min()</c> and <c>max()</c> call this. §min() and §max() each say "Comparison semantics are
    /// defined by the Comparison Operators for the type of value being aggregated", so they must not have
    /// an ordering rule of their own - which is exactly the mistake they used to make: a per-type ladder
    /// chosen from the collection's first element, with units matched by raw string equality and temporals
    /// re-parsed to <see cref="DateTime"/>. (The <c>aggregate(iif($total.empty(), $this, iif($this &lt;
    /// $total, $this, $total)))</c> fold is §aggregate()'s illustration of <em>that</em> function, not
    /// §min()'s definition, so it is not what this delegation rests on.)
    /// <paramref name="function"/> exists only so the resulting error names the caller.
    /// </remarks>
    public static int? CompareValues(IElement left, IElement right, string function)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return Compare(left, right, function, totalOrder: false);
    }

    /// <summary>
    /// Compares two values for <c>sort()</c>, always reaching a definite answer.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="function">The FHIRPath function requesting the comparison, for diagnostics.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    /// <exception cref="FhirPathEvaluationException">
    /// The operands are of types no conversion relates, or of a type FHIRPath defines no ordering for.
    /// </exception>
    public static int CompareForSort(IElement left, IElement right, string function)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return Compare(left, right, function, totalOrder: true)!.Value;
    }

    /// <summary>
    /// Applies FHIRPath equality to two operands, at least one of which is a Quantity.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>
    /// <see langword="true"/> or <see langword="false"/> when equality is decidable, and
    /// <see langword="null"/> when it is not: the units are incompatible, or an operand is not a value
    /// any reading makes a quantity.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Equality is a third policy over the same ladder as <see cref="CompareValues"/> and
    /// <see cref="CompareForSort"/>, not a comparison of its own, so a quantity cannot be equal on one
    /// surface and unequal on another. It exists because the callers collapse the undecided case
    /// differently: <c>=</c> yields empty, <c>~</c> yields <see langword="false"/>, and the collection
    /// functions - which have no third state - yield "not the same item".
    /// </para>
    /// <para>
    /// Without it <see cref="FunctionHelpers.AreElementsEqual"/> fell through to
    /// <see cref="object.Equals(object)"/> on the carrier, which compares the unit as text: <c>1 'm' =
    /// 100 'cm'</c> was <see langword="true"/> as an operator while <c>(1 'm' | 100 'cm').distinct()</c>
    /// returned two elements, and <c>1 'wk' in (7 'd')</c> was <see langword="false"/>.
    /// </para>
    /// </remarks>
    public static bool? AreQuantitiesEqual(IElement left, IElement right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftQuantity = AsQuantity(left);
        var rightQuantity = AsQuantity(right);

        if (leftQuantity is null || rightQuantity is null)
        {
            return null;
        }

        return CompareQuantityValues(leftQuantity, rightQuantity) is { } order ? order == 0 : null;
    }

    /// <summary>
    /// Compares two quantities by value, once both are expressed in one unit.
    /// </summary>
    /// <param name="left">The left quantity.</param>
    /// <param name="right">The right quantity.</param>
    /// <returns>
    /// A negative value, zero, a positive value, or <see langword="null"/> when the units are
    /// incompatible, which the spec answers with empty rather than with an ordering.
    /// </returns>
    public static int? CompareQuantityValues(FhirQuantity left, FhirQuantity right)
    {
        return TryAlignUnits(left, right, out var leftValue, out var rightValue)
            ? leftValue.CompareTo(rightValue)
            : null;
    }

    /// <summary>
    /// Expresses two quantities in a single unit, so that only their values remain to be compared.
    /// </summary>
    /// <param name="left">The left quantity, whose unit both values are expressed in.</param>
    /// <param name="right">The right quantity.</param>
    /// <param name="leftValue">The left value.</param>
    /// <param name="rightValue">The right value in the left's unit.</param>
    /// <returns><see langword="false"/> when no conversion relates the two units.</returns>
    /// <remarks>
    /// The one place the engine converts for a comparison. The ordering operators, equality, equivalence
    /// and the aggregates all reach it, which is what stops <c>1 'm'</c> and <c>100 'cm'</c> being one
    /// value to some of them and two to others. Equivalence needs the aligned values rather than the
    /// comparison result, because it rounds them to their stated precision first.
    /// </remarks>
    public static bool TryAlignUnits(FhirQuantity left, FhirQuantity right, out decimal leftValue, out decimal rightValue)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        leftValue = left.Value;
        rightValue = 0m;

        if (!UnitConverter.IsCompatible(left.Unit, right.Unit))
        {
            return false;
        }

        var converted = right.ConvertTo(left.Unit, UnitConverter);

        if (converted is null)
        {
            return false;
        }

        rightValue = converted.Value;
        return true;
    }

    /// <summary>
    /// Determines whether an operand is a Quantity, as opposed to a number a conversion could make one.
    /// </summary>
    /// <param name="element">The operand.</param>
    /// <returns><see langword="true"/> when the operand is a Quantity.</returns>
    /// <remarks>
    /// A resource-backed Quantity is a complex element whose own <see cref="IElement.Value"/> is
    /// <see langword="null"/> and whose value and unit live in its children, so testing the value alone
    /// misses every Quantity that came off the wire - which is why <c>Observation.value.min()</c> was
    /// empty while <c>Observation.value.first() &lt; 10 'g'</c> answered.
    /// </remarks>
    public static bool IsQuantity(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Value is FhirQuantity || QuantityEvaluator.IsQuantityInstanceType(element.InstanceType);
    }

    /// <summary>
    /// Reads an operand as a quantity, applying FHIRPath's implicit Integer/Decimal to Quantity conversion.
    /// </summary>
    /// <param name="element">The operand.</param>
    /// <returns>The quantity, or <see langword="null"/> when no reading of the operand is one.</returns>
    /// <remarks>
    /// The unity unit is what makes <c>1 'mg'</c> against <c>5</c> an incompatible-units case rather than
    /// a type error. <see cref="QuantityEvaluator.ExtractQuantity"/> forwards here so that the <c>&lt;</c>
    /// and <c>&gt;</c> operators, <c>sort()</c> and the aggregates cannot disagree about which operands
    /// are quantities - they previously did, over <see cref="double"/> and <see cref="float"/>.
    /// </remarks>
    public static FhirQuantity? AsQuantity(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.Value is FhirQuantity quantity)
        {
            return quantity;
        }

        if (QuantityEvaluator.IsQuantityInstanceType(element.InstanceType))
        {
            return QuantityEvaluator.ExtractQuantityFromChildren(element);
        }

        return TryToDecimal(element, out var number) ? new FhirQuantity(number, "1") : null;
    }

    /// <summary>
    /// Reads an operand as a <see cref="decimal"/> across FHIRPath's Integer, Long and Decimal types.
    /// </summary>
    /// <param name="element">The operand.</param>
    /// <param name="result">The value, or zero when the operand is not a number this type can hold.</param>
    /// <returns><see langword="true"/> when the operand was read.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="double"/> and <see cref="float"/> are read here, not merely widened for comparison. They
    /// reach <see cref="IElement.Value"/> by way of a JSON reader rather than from FHIRPath itself, whose
    /// only numeric types are Integer, Long and Decimal, but a decimal-typed element off the wire really
    /// can arrive as one and refusing it made <c>(1 'mg' | 2.5).min()</c> throw where <c>1 'mg' &lt;
    /// 2.5</c> answered. Values <see cref="decimal"/> cannot hold - including the non-finite ones - are
    /// refused rather than saturated; callers distinguish that from "not a number" through
    /// <see cref="IsNumericValued"/>.
    /// </para>
    /// <para>
    /// A <see cref="string"/> is read only when the element declares a numeric type. A FHIR decimal
    /// outside <see cref="decimal"/>'s range arrives that way - the JSON reader keeps the source text
    /// rather than losing the value - and refusing every string would have made those elements unorderable
    /// against ordinary decimals. The declared type is the gate: without it <c>('1' | 2).sort()</c> would
    /// quietly compare a String against an Integer, which FHIRPath makes an error.
    /// </para>
    /// </remarks>
    public static bool TryToDecimal(IElement element, out decimal result)
    {
        ArgumentNullException.ThrowIfNull(element);

        switch (element.Value)
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
            case double doubleValue:
                return TryNarrow(doubleValue, out result);
            case float floatValue:
                return TryNarrow(floatValue, out result);
            case string text when IsNumericInstanceType(element.InstanceType):
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            default:
                result = 0m;
                return false;
        }
    }

    /// <summary>
    /// Determines whether an operand is a number, whether or not <see cref="decimal"/> can hold it.
    /// </summary>
    /// <param name="element">The operand.</param>
    /// <returns><see langword="true"/> when the operand is numeric.</returns>
    /// <remarks>
    /// This separates arithmetic overflow, which FHIRPath answers with empty, from the Math section's
    /// "incompatible items", which is an error. <see cref="TryToDecimal"/> returns
    /// <see langword="false"/> for both.
    /// </remarks>
    public static bool IsNumericValued(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Value is int or long or decimal or double or float
            || (element.Value is string && IsNumericInstanceType(element.InstanceType));
    }

    /// <summary>
    /// Determines whether an operand is an Integer, so that a total stays one.
    /// </summary>
    /// <param name="element">The operand.</param>
    /// <returns><see langword="true"/> when the operand is an integer.</returns>
    public static bool IsIntegerValued(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Value is int or long
            || (element.Value is string && IsIntegerInstanceType(element.InstanceType));
    }

    /// <summary>
    /// Names an operand's type for an error message, adding the runtime type when it is not the one the
    /// declared type normally carries.
    /// </summary>
    /// <param name="element">The operand.</param>
    /// <returns>The type description.</returns>
    /// <remarks>
    /// A FHIR decimal too large for <see cref="decimal"/> arrives as a <see cref="string"/> under the
    /// declared type <c>decimal</c>, so naming the declared type alone produced "cannot order operands of
    /// type 'decimal' and 'decimal'" - true, and useless. This is deliberately not
    /// <see cref="FhirPathEvaluator.DescribeOperandType"/>, which the arithmetic errors use and which
    /// answers a different question: there both operands are readable and the declared type is the whole
    /// story.
    /// </remarks>
    public static string Describe(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var declared = element.InstanceType;
        var value = element.Value;

        if (declared is null)
        {
            return value?.GetType().Name ?? "unknown";
        }

        return value is null || IsExpectedRepresentation(declared, value)
            ? declared
            : $"{declared} ({value.GetType().Name})";
    }

    /// <summary>
    /// Builds the error for operands that have no ordering between them.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="function">The FHIRPath function requesting the comparison.</param>
    /// <returns>The exception to throw.</returns>
    public static FhirPathEvaluationException NotOrderable(IElement left, IElement right, string function)
    {
        return new FhirPathEvaluationException(
            $"{function} cannot order operands of type '{Describe(left)}' and '{Describe(right)}'.");
    }

    private static int? Compare(IElement left, IElement right, string function, bool totalOrder)
    {
        if (TemporalOperand.IsTemporal(left.Value, left.InstanceType)
            || TemporalOperand.IsTemporal(right.Value, right.InstanceType))
        {
            return CompareTemporals(left, right, function, totalOrder);
        }

        // FHIRPath's Comparison section defines the ordering operators for String, Integer, Long, Decimal,
        // Quantity, Date, DateTime and Time only. Boolean is not among them, and it reaches here as an
        // IComparable that would happily order false before true, so it has to be excluded by name.
        if (left.Value is bool || right.Value is bool)
        {
            throw NotOrderable(left, right, function);
        }

        if (IsQuantity(left) || IsQuantity(right))
        {
            return CompareQuantities(left, right, function, totalOrder);
        }

        if (TryCompareNumbers(left, right, out var numeric))
        {
            return numeric;
        }

        // A number that could not be read is still a number, whatever CLR type carried it. Without this the
        // two branches below would order a FHIR decimal that arrived as text - one outside decimal's range,
        // say - as though it were a String, silently and by its spelling.
        if (IsNumericValued(left) || IsNumericValued(right))
        {
            throw NotOrderable(left, right, function);
        }

        if (left.Value is string leftText && right.Value is string rightText)
        {
            return string.Compare(leftText, rightText, StringComparison.Ordinal);
        }

        // The non-generic IComparable is safe once the runtime types are known to match, which is the guard
        // the old comparers lacked: it is precisely the cross-type CompareTo that threw and got swallowed.
        // Every value this engine puts in IElement.Value that implements IComparable<T> also implements the
        // non-generic form, apart from FhirTemporal and FhirQuantity - and both are handled above. Same
        // runtime type on both sides makes this a total order within that type, so sort() may use it too.
        if (left.Value is not null
            && right.Value is not null
            && left.Value.GetType() == right.Value.GetType()
            && left.Value is IComparable comparable)
        {
            return comparable.CompareTo(right.Value);
        }

        throw NotOrderable(left, right, function);
    }

    /// <summary>
    /// Orders two temporals, reconciling a typed <see cref="FhirTemporal"/> against the raw string a
    /// FHIRPath <c>@</c>-literal still evaluates to.
    /// </summary>
    /// <remarks>
    /// The total order is <see cref="FhirTemporal.CompareTo"/> rather than a key assembled here.
    /// <see cref="FhirTemporal"/> already documents it as a total order whose zero coincides with
    /// <see cref="FhirTemporal.Equals(FhirTemporal)"/>, and it agrees with
    /// <see cref="FhirTemporal.Compare"/> wherever that is determinate: a definite <c>-1</c> there means
    /// the left interval ends before the right one begins, so the left instant is the earlier one and the
    /// primary key orders them the same way. Values the parser rejects have no instant at all, so they
    /// sort as a block after everything that has one, ordered among themselves by text.
    /// </remarks>
    private static int? CompareTemporals(IElement left, IElement right, string function, bool totalOrder)
    {
        var leftTemporal = TemporalOperand.AsTemporal(left.Value, left.InstanceType);
        var rightTemporal = TemporalOperand.AsTemporal(right.Value, right.InstanceType);

        if (leftTemporal is not null && rightTemporal is not null)
        {
            return totalOrder
                ? leftTemporal.CompareTo(rightTemporal)
                : FhirTemporal.Compare(leftTemporal, rightTemporal);
        }

        // One side is a temporal and the other is not a value any temporal reading reaches.
        if (!TemporalOperand.IsTemporal(left.Value, left.InstanceType)
            || !TemporalOperand.IsTemporal(right.Value, right.InstanceType))
        {
            throw NotOrderable(left, right, function);
        }

        // Both are declared temporal and at least one is malformed wire data, which is an expected input
        // rather than an ill-formed expression.
        if (!totalOrder)
        {
            return null;
        }

        if (leftTemporal is not null)
        {
            return -1;
        }

        if (rightTemporal is not null)
        {
            return 1;
        }

        return string.Compare(left.Value?.ToString(), right.Value?.ToString(), StringComparison.Ordinal);
    }

    private static int? CompareQuantities(IElement left, IElement right, string function, bool totalOrder)
    {
        var leftQuantity = AsQuantity(left);
        var rightQuantity = AsQuantity(right);

        if (leftQuantity is null || rightQuantity is null)
        {
            throw NotOrderable(left, right, function);
        }

        return totalOrder
            ? CompareQuantityKeys(leftQuantity, rightQuantity)
            : CompareQuantityValues(leftQuantity, rightQuantity);
    }

    /// <summary>
    /// Orders two quantities totally, by bucket first and canonical magnitude second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keying on the unit string would be intransitive: <c>1 'g' == 1000 'mg'</c> while
    /// <c>'g' &lt; 'm' &lt; 'mg'</c> as text. Bucketing first means every unit inside one bucket converts
    /// to every other, so the canonical magnitude is a total order there and cannot contradict a
    /// determinate comparison.
    /// </para>
    /// <para>
    /// The bucket is a function of the unit alone - see <see cref="CanonicalKey"/> - which is what makes
    /// that hold. It was a function of the value, and two quantities in the <em>same</em> unit could
    /// therefore key into different buckets: a magnitude whose conversion to the dimension's base unit
    /// overflowed <see cref="decimal"/> fell out of the canonical branch entirely. Two such values in
    /// commensurable-but-different units then ordered by their unit spellings, which inverted them.
    /// </para>
    /// <para>
    /// The residue is a tie, never an inversion. The magnitude is monotone in the true magnitude - it is
    /// the value times a per-unit scale, saturated at <see cref="decimal"/>'s bounds - so two quantities
    /// whose canonical magnitudes differ only above <c>decimal.MaxValue</c> or below <c>1e-28</c> key
    /// equal where <c>&gt;</c> orders them. That contradicts §sort()'s "Items are considered equal if and
    /// only if the equals (=) operator returns true" at those extremes, and is accepted rather than fixed:
    /// carrying the magnitude as a <see cref="double"/> would buy the range back and lose the middle,
    /// where <c>1 'm'</c> and <c>100 'cm'</c> stop keying equal because <c>0.01</c> has no exact binary
    /// form. A tie at 1e-28 is a better failure than an inversion at 1.
    /// </para>
    /// </remarks>
    private static int CompareQuantityKeys(FhirQuantity left, FhirQuantity right)
    {
        var leftKey = CanonicalKey(left);
        var rightKey = CanonicalKey(right);

        if (leftKey.IsCanonical != rightKey.IsCanonical)
        {
            return leftKey.IsCanonical ? -1 : 1;
        }

        var byBucket = string.Compare(leftKey.Bucket, rightKey.Bucket, StringComparison.Ordinal);

        return byBucket != 0 ? byBucket : leftKey.Magnitude.CompareTo(rightKey.Magnitude);
    }

    /// <summary>
    /// Reduces a quantity to a sort key: which comparable group it belongs to, and where in that group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scale comes from converting <em>one</em> of the unit, not the quantity's own value, so that
    /// the canonicity flag and the bucket depend on the unit and nothing else. Every pair of units
    /// <see cref="IQuantityUnitConverter.IsCompatible"/> relates therefore lands in one bucket, which is
    /// the property the ordering rests on.
    /// </para>
    /// <para>
    /// The non-canonical bucket is not simply the UCUM code. <c>a</c> and <c>mo</c> are not units UCUM
    /// cannot canonicalise - <c>GetDimensionality("a")</c> answers <c>"s"</c>, the same bucket as
    /// <c>wk</c> and <c>d</c>. They reach this branch because <see cref="QuantityUnitConverter.Convert"/>
    /// refuses to convert them at all, calendar years and months having no fixed length. That refusal
    /// also draws a line the UCUM code alone erases: it relates a calendar keyword only to another
    /// keyword, so <c>1 'year'</c> and <c>1 'a'</c> are not equal - <c>IsCompatible</c> is
    /// <see langword="false"/> and <c>1 'year' = 1 'a'</c> is empty - while
    /// <see cref="CalendarDuration.NormalizeToUcum"/> folds both to <c>"a"</c>. Keying on the folded code
    /// sorted them equal, contradicting §sort()'s "equal if and only if the equals (=) operator returns
    /// true". The keyword form is part of the bucket for that reason, and <c>'year'</c> and <c>'years'</c>
    /// still share one because they share both the code and the form.
    /// </para>
    /// </remarks>
    private static (bool IsCanonical, string Bucket, decimal Magnitude) CanonicalKey(FhirQuantity quantity)
    {
        var ucum = CalendarDuration.NormalizeToUcum(quantity.Unit);
        var dimension = UnitConverter.GetDimensionality(ucum);
        var scale = dimension is null ? null : UnitConverter.Convert(1m, ucum, dimension);

        return scale is null
            ? (false, CalendarDuration.IsCalendarKeyword(quantity.Unit) ? ucum + " calendar" : ucum, quantity.Value)
            : (true, dimension!, Rescale(quantity.Value, scale.Value));
    }

    /// <summary>
    /// Expresses a value in its dimension's base unit, saturating rather than throwing.
    /// </summary>
    /// <remarks>
    /// Saturation keeps the key monotone in the true magnitude, so an out-of-range product costs a tie
    /// rather than an inversion. Falling back to a different bucket - which is what the old
    /// value-conversion did, by way of a <see langword="null"/> - cost an inversion, because the bucket
    /// then no longer grouped commensurable units. Caught rather than predicted because there is no
    /// cheaper test for <see cref="decimal"/> multiplication overflow than the multiplication.
    /// </remarks>
    private static decimal Rescale(decimal value, decimal scale)
    {
        try
        {
            return value * scale;
        }
        catch (OverflowException)
        {
            return value < 0m ? decimal.MinValue : decimal.MaxValue;
        }
    }

    /// <summary>
    /// Compares two numbers across the integer and decimal types, so that <c>1</c> and <c>1.0</c> order
    /// by value rather than by CLR type.
    /// </summary>
    /// <remarks>
    /// A <see cref="double"/> operand demotes the whole comparison to binary floating point rather than
    /// widening to <see cref="decimal"/>, because the decimal range does not cover the double range and
    /// the conversion would refuse the operand outright.
    /// </remarks>
    private static bool TryCompareNumbers(IElement left, IElement right, out int result)
    {
        if (left.Value is double or float || right.Value is double or float)
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

    private static bool TryToDouble(IElement element, out double result)
    {
        switch (element.Value)
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
            case string text when IsNumericInstanceType(element.InstanceType):
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            default:
                result = 0d;
                return false;
        }
    }

    /// <summary>
    /// Narrows a binary floating-point value to <see cref="decimal"/>, refusing the values the cast
    /// would throw on.
    /// </summary>
    /// <param name="value">The value to narrow.</param>
    /// <param name="result">The narrowed value, or zero when it was refused.</param>
    /// <returns><see langword="false"/> for a non-finite or out-of-range value.</returns>
    /// <remarks>
    /// Shared with <see cref="QuantityEvaluator.ExtractQuantityFromChildren"/> so that a resource-backed
    /// Quantity and a bare number refuse the same values. A plain <c>(decimal)</c> cast throws
    /// <see cref="OverflowException"/> for NaN, the infinities and anything outside
    /// <see cref="decimal"/>'s range, which on a shared read path escapes the aggregates' overflow
    /// <c>catch</c> entirely.
    /// </remarks>
    internal static bool TryNarrow(double value, out decimal result)
    {
        if (!double.IsFinite(value) || value < (double)decimal.MinValue || value > (double)decimal.MaxValue)
        {
            result = 0m;
            return false;
        }

        result = (decimal)value;
        return true;
    }

    private static bool IsExpectedRepresentation(string declared, object value) => value switch
    {
        bool => declared.Equals("boolean", StringComparison.OrdinalIgnoreCase),
        int or long or decimal or double or float => IsNumericInstanceType(declared),
        FhirQuantity => QuantityEvaluator.IsQuantityInstanceType(declared),
        FhirTemporal => TemporalOperand.IsTemporal(null, declared),

        // Every other FHIR primitive reaches IElement.Value as a string, and so - per ADR-2610 - does a
        // FHIRPath @-literal, which carries a temporal declared type over a raw literal.
        string => !IsNumericInstanceType(declared) && !declared.Equals("boolean", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static bool IsNumericInstanceType(string? instanceType)
    {
        return instanceType is not null
            && (instanceType.Equals("decimal", StringComparison.OrdinalIgnoreCase) || IsIntegerInstanceType(instanceType));
    }

    private static bool IsIntegerInstanceType(string? instanceType)
    {
        return instanceType is not null
            && (instanceType.Equals("integer", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("integer64", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("positiveInt", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("unsignedInt", StringComparison.OrdinalIgnoreCase));
    }
}
