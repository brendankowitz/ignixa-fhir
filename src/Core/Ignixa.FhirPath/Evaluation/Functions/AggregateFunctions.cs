/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * sum(), min(), max() and avg(), all built on the one comparison rule in SortComparer.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Types;

namespace Ignixa.FhirPath.Evaluation.Functions;

#nullable enable

/// <summary>
/// The FHIRPath aggregate functions over numbers, quantities, strings and temporals.
/// </summary>
/// <remarks>
/// <para>
/// FHIRPath does not define these primitively. It defines them in terms of <c>aggregate()</c> and the
/// ordinary operators - <c>value.aggregate($this + $total, 0)</c> for the sum,
/// <c>value.aggregate(iif($total.empty(), $this, iif($this &lt; $total, $this, $total)))</c> for the
/// minimum - so their semantics are whatever <c>+</c>, <c>&lt;</c> and <c>&gt;</c> already say. They are
/// implemented directly here for the arithmetic, but the ordering is delegated to
/// <see cref="SortComparer.CompareValues"/> rather than restated.
/// </para>
/// <para>
/// The restatement is what went wrong. Each function used to pick a per-type branch from
/// <c>list[0].Value</c>, so a collection whose head was an integer was totalled as integers and every
/// quantity in it was silently dropped; quantity units were matched with <c>==</c>, so
/// <c>(1 'm' | 50 'cm').min()</c> was empty; and temporals were re-parsed with
/// <c>DateTime.TryParseExact</c> against a fixed format list, which discarded partial precision
/// (<c>@2012</c> matched no format at all and was skipped) and equated a floating local time with a
/// fixed instant.
/// </para>
/// </remarks>
internal static class AggregateFunctions
{
    private static readonly IQuantityUnitConverter UnitConverter = QuantityUnitConverter.Instance;

    /// <summary>
    /// Computes the sum of a collection of numeric values or quantities.
    /// </summary>
    /// <param name="elements">Collection to sum</param>
    /// <returns>The total, or empty when the units do not relate.</returns>
    [FhirPathFunction("sum",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Aggregate",
        Description = "Computes the sum of a collection of numeric values or quantities")]
    public static IEnumerable<IElement> Sum(IEnumerable<IElement> elements)
    {
        // The spec's equivalent form seeds aggregate() with 0, so an empty collection totals to 0 rather
        // than to empty - which is why sum() is the one aggregate here that answers a non-empty result
        // for no input. That seed is only correct for a collection that is genuinely empty, though:
        // Patient.name is two elements that happen to carry no value, and reporting its total as 0 would
        // be an answer to a question nobody asked.
        var received = elements.Where(element => element is not null).ToList();

        if (received.Count == 0)
        {
            return [FunctionHelpers.CreateInteger(0)];
        }

        var list = Materialize(received);

        if (list.Count == 0)
        {
            return [];
        }

        if (list.Count == 1)
        {
            return [list[0]];
        }

        return Total(list, "sum()", average: false);
    }

    /// <summary>
    /// Finds the minimum value in a collection.
    /// </summary>
    /// <param name="elements">Collection to evaluate</param>
    /// <returns>The least element, or empty.</returns>
    [FhirPathFunction("min",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Aggregate",
        Description = "Finds the minimum value in a collection")]
    public static IEnumerable<IElement> Min(IEnumerable<IElement> elements) => Extreme(elements, "min()", isMax: false);

    /// <summary>
    /// Finds the maximum value in a collection.
    /// </summary>
    /// <param name="elements">Collection to evaluate</param>
    /// <returns>The greatest element, or empty.</returns>
    [FhirPathFunction("max",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Aggregate",
        Description = "Finds the maximum value in a collection")]
    public static IEnumerable<IElement> Max(IEnumerable<IElement> elements) => Extreme(elements, "max()", isMax: true);

    /// <summary>
    /// Computes the average of a collection of numeric values or quantities.
    /// </summary>
    /// <param name="elements">Collection to average</param>
    /// <returns>The mean, or empty when the units do not relate.</returns>
    [FhirPathFunction("avg",
        SupportedContexts = "any-any",
        ReturnType = "decimal",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Aggregate",
        Description = "Computes the average of a collection of numeric values or quantities")]
    public static IEnumerable<IElement> Avg(IEnumerable<IElement> elements)
    {
        var list = Materialize(elements);

        if (list.Count == 0)
        {
            return [];
        }

        // avg() answers Decimal, so a lone Integer is promoted; a lone Quantity or Decimal already is one.
        if (list.Count == 1)
        {
            return [list[0].Value is int single ? CreateDecimal(single) : list[0]];
        }

        return Total(list, "avg()", average: true);
    }

    /// <summary>
    /// Selects the least or greatest element of a collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The winning <see cref="IElement"/> is returned as-is rather than reconstructed. Rebuilding it
    /// re-typed a resource-backed <see cref="FhirTemporal"/> back down to a wire string, so everything
    /// downstream that dispatches on the typed value - arithmetic, ordering, boundary functions - silently
    /// fell through to its string branch. It also settles which unit the extreme of a mixed-unit
    /// collection comes back in: its own. <c>(1 'm' | 50 'cm').min()</c> is <c>50 'cm'</c>, because
    /// selecting is all these two functions do. <c>sum()</c> and <c>avg()</c> have to construct a value
    /// and so must name a unit; they use the first operand's.
    /// </para>
    /// <para>
    /// An indeterminate comparison abandons the whole result. There is no incumbent to fall back on the
    /// way <c>sort()</c> falls back on stable order: if the candidate and the running extreme cannot be
    /// ordered against each other then neither of them is demonstrably the extreme, and answering with
    /// one of them would be a guess dressed as an answer. Empty is also what the collection functions
    /// reach for elsewhere when FHIRPath declines to decide - incompatible quantity units are the spec's
    /// own empty, and overlapping partial-precision temporals are indeterminate by construction.
    /// </para>
    /// </remarks>
    private static IEnumerable<IElement> Extreme(IEnumerable<IElement> elements, string function, bool isMax)
    {
        var list = Materialize(elements);

        if (list.Count == 0)
        {
            return [];
        }

        var extreme = list[0];

        for (var index = 1; index < list.Count; index++)
        {
            var comparison = SortComparer.CompareValues(list[index], extreme, function);

            if (comparison is null)
            {
                return [];
            }

            if (isMax ? comparison > 0 : comparison < 0)
            {
                extreme = list[index];
            }
        }

        return [extreme];
    }

    /// <summary>
    /// Totals a collection, optionally dividing by its size.
    /// </summary>
    /// <remarks>
    /// The presence of any Quantity - not the type of the head - selects the quantity path, and plain
    /// numbers reaching it are read as the unity quantity FHIRPath's implicit conversion gives them. That
    /// is what makes <c>(1 | 5 'mg').sum()</c> an incompatible-units empty rather than the <c>1</c> that
    /// dispatching on <c>list[0]</c> used to return.
    /// </remarks>
    private static IEnumerable<IElement> Total(List<IElement> list, string function, bool average)
    {
        return list.Exists(element => element.Value is FhirQuantity)
            ? TotalQuantities(list, function, average)
            : TotalNumbers(list, function, average);
    }

    /// <summary>
    /// Totals a collection of quantities, converting each into the first operand's unit.
    /// </summary>
    private static IEnumerable<IElement> TotalQuantities(List<IElement> list, string function, bool average)
    {
        var unit = AsQuantity(list[0], function).Unit;
        decimal total = 0;

        foreach (var element in list)
        {
            // ConvertTo returns the value untouched for an exact unit match, so a unit UCUM has never
            // heard of still totals against itself rather than collapsing to empty.
            var converted = AsQuantity(element, function).ConvertTo(unit, UnitConverter);

            if (converted is null)
            {
                return [];
            }

            total += converted.Value;
        }

        return [FunctionHelpers.CreateQuantity(new FhirQuantity(average ? total / list.Count : total, unit))];
    }

    /// <summary>
    /// Totals a collection of numbers, answering Integer only when every operand and the result are.
    /// </summary>
    private static IEnumerable<IElement> TotalNumbers(List<IElement> list, string function, bool average)
    {
        decimal total = 0;
        var hasFraction = false;

        foreach (var element in list)
        {
            if (!TryReadNumber(element.Value, out var number))
            {
                throw NotSummable(element, function);
            }

            hasFraction |= element.Value is decimal or double or float;
            total += number;
        }

        if (average)
        {
            return [CreateDecimal(total / list.Count)];
        }

        if (hasFraction || total < int.MinValue || total > int.MaxValue)
        {
            return [CreateDecimal(total)];
        }

        return [CreateInteger((int)total)];
    }

    /// <summary>
    /// Drops the elements that carry no value, so that a comparison never has to answer for a null.
    /// </summary>
    private static List<IElement> Materialize(IEnumerable<IElement> elements)
    {
        return elements.Where(element => element?.Value is not null).ToList();
    }

    private static FhirQuantity AsQuantity(IElement element, string function)
    {
        return SortComparer.AsQuantity(element.Value!) ?? throw NotSummable(element, function);
    }

    /// <summary>
    /// Reads a value as a <see cref="decimal"/> contribution to a total.
    /// </summary>
    /// <remarks>
    /// The Integer and Decimal cases are <see cref="SortComparer.TryToDecimal"/>'s, so the engine has one
    /// answer to "which CLR types are a FHIRPath number". The bridge from <see cref="double"/> is added
    /// here and only here: FHIRPath's own numeric type is Decimal and a double reaches
    /// <see cref="IElement.Value"/> only by way of a JSON reader, but a decimal-typed element read off the
    /// wire really can arrive as one, and refusing to total it would fail on real data. Values outside
    /// decimal's range are refused rather than saturated, because a truncated total is a wrong answer
    /// where an error is a visible one.
    /// </remarks>
    private static bool TryReadNumber(object? value, out decimal result)
    {
        if (value is not null && SortComparer.TryToDecimal(value, out result))
        {
            return true;
        }

        result = 0m;

        var widened = value switch
        {
            double number => number,
            float number => number,
            _ => (double?)null
        };

        if (widened is null || widened.Value < (double)decimal.MinValue || widened.Value > (double)decimal.MaxValue)
        {
            return false;
        }

        result = (decimal)widened.Value;
        return true;
    }

    /// <summary>
    /// Reports an operand that has no arithmetic relating it to the rest of the collection.
    /// </summary>
    /// <remarks>
    /// This is an error rather than an empty because FHIRPath's <c>+</c> is an error across unrelated
    /// types, and these functions are defined in terms of it. The empty result is reserved for the case
    /// the spec actually assigns it to - operands that are the right type but whose units do not relate -
    /// so the two outcomes stay distinguishable instead of both meaning "something went wrong".
    /// </remarks>
    private static FhirPathEvaluationException NotSummable(IElement element, string function)
    {
        return new FhirPathEvaluationException(
            $"{function} cannot total an operand of type '{FhirPathEvaluator.DescribeOperandType(element)}'.");
    }

    private static IElement CreateInteger(int value) => new FunctionHelpers.PrimitiveElement(value, "integer");

    private static IElement CreateDecimal(decimal value) => new FunctionHelpers.PrimitiveElement(value, "decimal");
}
