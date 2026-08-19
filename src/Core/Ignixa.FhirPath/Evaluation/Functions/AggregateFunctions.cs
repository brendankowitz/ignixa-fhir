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
/// Spec references throughout this file are to the FHIRPath continuous build off <c>master</c>
/// (<c>HL7/FHIRPath</c>, <c>input/pages/index.md</c>), checked 2026-08-19. It is unreleased and
/// versioned 3.0.0-ballot in places; the released Normative 2.0.0 has no <c>sum()</c>, <c>min()</c>,
/// <c>max()</c>, <c>avg()</c> or <c>sort()</c> at all. Everything in §Aggregates is marked
/// <c>{:.stu}</c> - Standard for Trial Use - so these citations can move under us, which is why they
/// carry a date.
/// </para>
/// <para>
/// The spec gives each function a dedicated section and states its semantics there. It does not define
/// them in terms of <c>aggregate()</c>: the equivalent forms - <c>value.aggregate($this + $total, 0)</c>
/// and the <c>iif</c> fold for the minimum - appear under §aggregate() as illustrations of that
/// function, not as the definition of these. The ordering is nonetheless delegated to
/// <see cref="ValueOrdering.CompareValues"/> rather than restated, on §min()'s own instruction:
/// "Comparison semantics are defined by the Comparison Operators for the type of value being
/// aggregated."
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
/// <em>Deliberate divergence.</em> All four sections carry the sentence "All items in the input
/// collection SHALL be the same type, otherwise an exception is thrown", and this implementation does
/// not throw for every collection that fails a literal reading of it. It cannot: §avg() in the same
/// section requires that "When used with Integer or Long, the arguments will be implicitly converted to
/// Decimal before evaluation", and §Math requires <c>3 'm' + 3 'cm'</c> to add, so "the same type"
/// cannot mean the same declared type without contradicting the spec's own worked examples. The rule
/// applied here is the SHALL read <em>after</em> FHIRPath's implicit conversions: Integer, Long and
/// Decimal are one numeric type, a number is a Quantity in the unity unit, and operands that no
/// conversion relates - a String beside an Integer - do throw. What is knowingly not implemented is a
/// stricter reading under which <c>(1 | 2.5).sum()</c> would be an error.
/// </para>
/// <para>
/// Arithmetic overflow yields empty rather than an error, matching §Math ("Operations that cause
/// arithmetic overflow or underflow will result in empty ({ })") and matching
/// <see cref="FhirPathEvaluator"/>, which catches <see cref="OverflowException"/> around the operators.
/// A value of a type no arithmetic relates is still an error; the two outcomes are kept
/// distinguishable rather than both meaning "something went wrong".
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
    /// <remarks>
    /// §sum(): "If the input collection is empty ({ }), the result is empty." This previously answered
    /// <c>0</c>, on the strength of the <c>0</c> seed in §aggregate()'s illustrative
    /// <c>value.aggregate($this + $total, 0)</c>. The dedicated section is the definition and the
    /// illustration is not, so the seed does not override it - and answering <c>0</c> made <c>sum()</c>
    /// the one aggregate that reported a confident number about a collection it had never seen a value
    /// from. The official HL7 suite has no <c>{}.sum()</c> case, in r4 or r5, so nothing pinned either
    /// reading; checked 2026-08-19.
    /// </remarks>
    public static IEnumerable<IElement> Sum(IEnumerable<IElement> elements)
    {
        return Total(Materialize(elements), "sum()", average: false);
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
        return Total(Materialize(elements), "avg()", average: true);
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
    /// <para>
    /// The presence of any Quantity - not the type of the head - selects the quantity path, and plain
    /// numbers reaching it are read as the unity quantity FHIRPath's implicit conversion gives them. That
    /// is what makes <c>(1 | 5 'mg').sum()</c> an incompatible-units empty rather than the <c>1</c> that
    /// dispatching on <c>list[0]</c> used to return.
    /// </para>
    /// <para>
    /// A one-element collection has no shortcut. It used to: both callers checked
    /// <see cref="ValueOrdering.IsNumericValued"/> and then handed the element straight back, which
    /// admits a <see cref="string"/> under the declared type <c>decimal</c> - how a FHIR decimal too
    /// large for <see cref="decimal"/> arrives off the wire. One such element made <c>avg()</c> return
    /// the raw string, contradicting its own "avg() answers Decimal" contract, while two of them
    /// returned empty. Same data, different answer by cardinality - the exact asymmetry the type gate
    /// was added to close. A lone <see cref="double"/> came back un-promoted for the same reason. The
    /// arithmetic path is the only reading of the type gate that cannot drift from the pair case,
    /// because it is the pair case.
    /// </para>
    /// </remarks>
    private static IEnumerable<IElement> Total(List<IElement> list, string function, bool average)
    {
        if (list.Count == 0)
        {
            return [];
        }

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
    /// <para>
    /// §Math: "When the units of quantity arguments are different, the quantity values must be converted
    /// to the most granular unit, then simple addition on the values can be performed", with the worked
    /// example <c>3 'm' + 3 'cm' // 303 'cm'</c>.
    /// </para>
    /// <para>
    /// §Unit Conversions supplies the granularity test, and it is the one implemented here: "This can be
    /// generically evaluated by selecting the conversion factor that is less than 1 when converting from
    /// one unit to the other. If the conversion factor is greater than 1, then the other unit is more
    /// granular." The equal case is named there too - "If the conversion factors are 1 (the units are
    /// equal), then choose the unit of the operator's left argument" - which is why a factor of exactly
    /// 1 leaves the incumbent standing rather than taking the candidate.
    /// </para>
    /// <para>
    /// The first operand's unit was used instead, which agreed with the spec only when the head happened
    /// to be the finest unit - which is why the one pre-existing mixed-unit test,
    /// <c>((5 'mg') | (1 'kg')).sum()</c>, could not tell the two rules apart.
    /// </para>
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
