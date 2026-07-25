using Ignixa.Search.Extensions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the comparator-dependent predicate shared by Number and Quantity leaf lowering — both store
/// LowValue/HighValue with identical range semantics.
/// <para>
/// Every comparator applies the relation the FHIR search prefix table (search.html) defines between the
/// parameter range [S, E] and the resource range [LowValue, HighValue], so numbers, quantities, and dates
/// share one set of semantics — see <see cref="DateTimeRangeComparison"/> for the same table over
/// [StartDateTime, EndDateTime]:
/// <c>gt: High &gt; E</c>, <c>ge: High &gt;= S</c>, <c>lt: Low &lt; S</c>, <c>le: Low &lt;= E</c>,
/// <c>sa: Low &gt; E</c>, <c>eb: High &lt; S</c>. Note which column each one names: <c>gt</c> and
/// <c>sa</c> are different relations (as are <c>lt</c> and <c>eb</c>), and the ordering comparators never
/// compare against the near column, or a row whose range straddles the search value would be missed.
/// </para>
/// <para>
/// The ordering comparators each constrain the bound the spec's intersection test names, which is the
/// OPPOSITE bound to the direction of the operator: <c>gt</c>/<c>ge</c> ask whether the range above the
/// search value reaches the target at all, which is true when the target's HighValue clears it;
/// <c>lt</c>/<c>le</c> likewise test LowValue. <c>sa</c>/<c>eb</c> ask whether the target lies wholly to
/// one side, so they take the other bound: <c>sa</c> is LowValue, <c>eb</c> is HighValue. Reading the
/// operator as though it applied to the near bound (gt on LowValue) silently implements <c>sa</c>/
/// <c>eb</c> semantics instead, which agrees with the spec only for point-valued rows.
/// </para>
/// <para>
/// What differs between comparators is only what [S, E] is. <c>eq</c>/<c>ne</c> widen the search value by
/// the FHIR implied-decimal-precision tolerance, and <c>:ap</c> by
/// <c>max(precision_modifier, abs(value) × 0.10)</c>. The ordering comparators do not widen at all: the
/// spec states that for <c>lt</c>/<c>le</c>/<c>gt</c>/<c>ge</c> "the implicit precision of the number is
/// ignored, and they are treated as if they have arbitrarily high precision", i.e. <c>gt100</c> means
/// greater than exactly 100, so S = E = value.
/// </para>
/// <para>
/// <c>eq</c> is CONTAINMENT — the parameter range fully contains the resource range. <c>ne</c> is the
/// exact negation of that containment, which makes <c>eq</c> and <c>ne</c> genuine complements: every
/// stored row satisfies exactly one of them. <c>ap</c> is OVERLAP against the widened bounds, matching
/// <see cref="DateTimeRangeComparison"/>'s <c>ap</c> — a deliberately looser relation than <c>eq</c>.
/// </para>
/// <para>
/// For a point-valued row (LowValue = HighValue, what a plain <c>valueQuantity</c> or number indexes to)
/// containment and overlap coincide, and so do all six ordering comparators' two candidate columns; the
/// distinction only bites on a row that stores a genuine range, such as an indexed <c>Range</c> element,
/// or one the row generator half-bounded with a sentinel.
/// </para>
/// </summary>
internal static class NumericRangeComparison
{
    public static Predicate Build(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, SearchComparator comparator, decimal value) => comparator switch
    {
        SearchComparator.Eq => BuildEq(context, lowColumn, highColumn, value),
        SearchComparator.Ne => BuildNe(context, lowColumn, highColumn, value),
        SearchComparator.Gt => new Predicate.GreaterThan(highColumn, context.Parameter(value)),
        SearchComparator.Ge => new Predicate.GreaterThanOrEqual(highColumn, context.Parameter(value)),
        SearchComparator.Lt => new Predicate.LessThan(lowColumn, context.Parameter(value)),
        SearchComparator.Le => new Predicate.LessThanOrEqual(lowColumn, context.Parameter(value)),
        SearchComparator.Sa => new Predicate.GreaterThan(lowColumn, context.Parameter(value)),
        SearchComparator.Eb => new Predicate.LessThan(highColumn, context.Parameter(value)),
        SearchComparator.Ap => BuildApproximate(context, lowColumn, highColumn, value),
        _ => throw new NotSupportedException($"Unknown SearchComparator '{comparator}'."),
    };

    /// <summary>
    /// <c>eq</c> is containment: the widened window [value - modifier, value + modifier] must contain
    /// [LowValue, HighValue].
    /// </summary>
    /// <remarks>
    /// A window edge that is not representable as a decimal is dropped rather than computed, because the
    /// subtraction would throw <see cref="OverflowException"/> on plain user input
    /// (<c>?value-quantity=eq79228162514264337593543950335</c>). Dropping it is exact, not an
    /// approximation: no stored value can lie beyond <see cref="decimal.MaxValue"/>, so the constraint it
    /// would express is satisfied by every row. Only one edge can ever be unrepresentable — the modifier
    /// is at most 0.5 and the decimal range is symmetric — so the two guards cannot both fire.
    /// </remarks>
    private static Predicate BuildEq(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, decimal value)
    {
        var modifier = value.GetPrescisionModifier();

        if (value < decimal.MinValue + modifier)
        {
            return new Predicate.LessThanOrEqual(highColumn, context.Parameter(value + modifier));
        }

        if (value > decimal.MaxValue - modifier)
        {
            return new Predicate.GreaterThanOrEqual(lowColumn, context.Parameter(value - modifier));
        }

        return new Predicate.And(
            new Predicate.GreaterThanOrEqual(lowColumn, context.Parameter(value - modifier)),
            new Predicate.LessThanOrEqual(highColumn, context.Parameter(value + modifier)));
    }

    /// <summary>
    /// <c>ne</c> is the exact negation of <see cref="BuildEq"/>'s containment, built by De Morgan so the
    /// two partition every row. The same edge-representability guards apply, negated: an
    /// <c>eq</c> constraint that every row satisfies negates to an <c>ne</c> disjunct that no row
    /// satisfies, so it is dropped from the Or rather than computed.
    /// </summary>
    private static Predicate BuildNe(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, decimal value)
    {
        var modifier = value.GetPrescisionModifier();

        if (value < decimal.MinValue + modifier)
        {
            return new Predicate.GreaterThan(highColumn, context.Parameter(value + modifier));
        }

        if (value > decimal.MaxValue - modifier)
        {
            return new Predicate.LessThan(lowColumn, context.Parameter(value - modifier));
        }

        return new Predicate.Or(
            new Predicate.LessThan(lowColumn, context.Parameter(value - modifier)),
            new Predicate.GreaterThan(highColumn, context.Parameter(value + modifier)));
    }

    private static Predicate BuildApproximate(
        LeafContext context,
        SqlColumnRef lowColumn,
        SqlColumnRef highColumn,
        decimal value)
    {
        var tolerance = Math.Max(value.GetPrescisionModifier(), Math.Abs(value) * 0.10m);
        var hasLowerBound = value >= decimal.MinValue + tolerance;
        var hasUpperBound = value <= decimal.MaxValue - tolerance;

        if (!hasLowerBound)
        {
            return new Predicate.LessThanOrEqual(lowColumn, context.Parameter(value + tolerance));
        }

        if (!hasUpperBound)
        {
            return new Predicate.GreaterThanOrEqual(highColumn, context.Parameter(value - tolerance));
        }

        return new Predicate.And(
            new Predicate.LessThanOrEqual(lowColumn, context.Parameter(value + tolerance)),
            new Predicate.GreaterThanOrEqual(highColumn, context.Parameter(value - tolerance)));
    }
}
