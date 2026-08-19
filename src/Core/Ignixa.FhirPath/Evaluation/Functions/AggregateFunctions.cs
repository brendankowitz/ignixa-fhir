/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * sum(), min(), max() and avg(), all built on the one comparison rule in ValueOrdering.
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
/// <see cref="ValueOrdering.CompareValues"/> rather than restated.
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
/// <para>
/// Arithmetic overflow yields empty rather than an error, matching §Math ("Operations that cause
/// arithmetic overflow or underflow will result in empty ({ })") and matching
/// <see cref="FhirPathEvaluator"/>, which catches <see cref="OverflowException"/> around the operators
/// these functions are defined in terms of. A value of a type no arithmetic relates is still an error;
/// the two outcomes are kept distinguishable rather than both meaning "something went wrong".
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
        const string Function = "sum()";

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
            // The type gate is the collection's, not the pair's: 'apple' is no more summable alone than it
            // is beside a number, and routing the single-element case around the check made the same
            // expression answer or throw depending only on how many items reached it.
            EnsureSummable(list[0], Function);
            return [list[0]];
        }

        return Total(list, Function, average: false);
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
        const string Function = "avg()";

        var list = Materialize(elements);

        if (list.Count == 0)
        {
            return [];
        }

        // avg() answers Decimal, so a lone Integer is promoted; a lone Quantity or Decimal already is one.
        if (list.Count == 1)
        {
            EnsureSummable(list[0], Function);

            return
            [
                list[0].Value switch
                {
                    int intValue => CreateDecimal(intValue),
                    long longValue => CreateDecimal(longValue),
                    _ => list[0]
                }
            ];
        }

        return Total(list, Function, average: true);
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
    /// and so must name a unit.
    /// </para>
    /// <para>
    /// An indeterminate comparison leaves the incumbent standing rather than abandoning the result. This
    /// is the spec's own equivalence, not a choice: <c>iif($this &lt; $total, $this, $total)</c> takes the
    /// otherwise-branch when the criterion is empty, so the fold yields <c>$total</c> and never yields
    /// empty. Abandoning was also wrong on its own terms - <c>(@2011 | @2012 | @2012-06-15).min()</c> was
    /// <c>@2011</c> but <c>(@2012 | @2012-06-15 | @2011).min()</c> was empty, from a collection whose
    /// minimum is the same element either way and is determinately less than both of the others.
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
            var comparison = ValueOrdering.CompareValues(list[index], extreme, function);

            if (comparison is not null && (isMax ? comparison > 0 : comparison < 0))
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
        try
        {
            return list.Exists(ValueOrdering.IsQuantity)
                ? TotalQuantities(list, function, average)
                : TotalNumbers(list, function, average);
        }
        catch (OverflowException)
        {
            return [];
        }
    }

    /// <summary>
    /// Totals a collection of quantities, converting each into the most granular unit present.
    /// </summary>
    private static IEnumerable<IElement> TotalQuantities(List<IElement> list, string function, bool average)
    {
        var unit = MostGranularUnit(list, function);

        if (unit is null)
        {
            return [];
        }

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
    /// Chooses the unit a constructed total is expressed in.
    /// </summary>
    /// <returns>The unit, or <see langword="null"/> when the collection's units do not all relate.</returns>
    /// <remarks>
    /// §Math: "The unit of the result will be the most granular unit of either input", with the worked
    /// example <c>3 'm' + 3 'cm' // 303 'cm'</c>. The first operand's unit was used instead, which agreed
    /// with the spec only when the head happened to be the finest unit - which is why the one pre-existing
    /// mixed-unit test, <c>((5 'mg') | (1 'kg')).sum()</c>, could not tell the two rules apart. Granularity
    /// is read by converting one of the candidate unit into the incumbent: a unit is finer exactly when one
    /// of it is worth less than one of the other.
    /// </remarks>
    private static string? MostGranularUnit(List<IElement> list, string function)
    {
        var unit = AsQuantity(list[0], function).Unit;

        for (var index = 1; index < list.Count; index++)
        {
            var candidate = AsQuantity(list[index], function).Unit;
            var candidateInUnit = UnitConverter.Convert(1m, candidate, unit);

            if (candidateInUnit is null)
            {
                return null;
            }

            if (candidateInUnit.Value < 1m)
            {
                unit = candidate;
            }
        }

        return unit;
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
            if (!ValueOrdering.TryToDecimal(element, out var number))
            {
                // A number decimal cannot hold - including a non-finite double - is an arithmetic
                // overflow, which FHIRPath answers with empty. Anything that is not a number at all is
                // §Math's "incompatible items", which is an error.
                if (ValueOrdering.IsNumericValued(element))
                {
                    return [];
                }

                throw NotSummable(element, function);
            }

            hasFraction |= !ValueOrdering.IsIntegerValued(element);
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
    /// Drops the elements that carry nothing to aggregate.
    /// </summary>
    /// <remarks>
    /// A resource-backed Quantity carries no <see cref="IElement.Value"/> of its own - its value and unit
    /// are children - so screening on the value alone discarded every quantity that came off the wire
    /// before the quantity path could read it, and <c>Observation.value.sum()</c> was empty on data whose
    /// individual elements compared fine.
    /// </remarks>
    private static List<IElement> Materialize(IEnumerable<IElement> elements)
    {
        return elements
            .Where(element => element is not null && (element.Value is not null || ValueOrdering.AsQuantity(element) is not null))
            .ToList();
    }

    private static FhirQuantity AsQuantity(IElement element, string function)
    {
        return ValueOrdering.AsQuantity(element) ?? throw NotSummable(element, function);
    }

    private static void EnsureSummable(IElement element, string function)
    {
        if (!ValueOrdering.IsQuantity(element) && !ValueOrdering.IsNumericValued(element))
        {
            throw NotSummable(element, function);
        }
    }

    /// <summary>
    /// Reports an operand that has no arithmetic relating it to the rest of the collection.
    /// </summary>
    /// <remarks>
    /// This is an error rather than an empty because FHIRPath's <c>+</c> is an error across unrelated
    /// types, and these functions are defined in terms of it. The empty result is reserved for the cases
    /// the spec actually assigns it to - operands that are the right type but whose units do not relate,
    /// and arithmetic overflow - so the outcomes stay distinguishable instead of all meaning "something
    /// went wrong".
    /// </remarks>
    private static FhirPathEvaluationException NotSummable(IElement element, string function)
    {
        return new FhirPathEvaluationException(
            $"{function} cannot total an operand of type '{ValueOrdering.Describe(element)}'.");
    }

    private static IElement CreateInteger(int value) => new FunctionHelpers.PrimitiveElement(value, "integer");

    private static IElement CreateDecimal(decimal value) => new FunctionHelpers.PrimitiveElement(value, "decimal");
}
