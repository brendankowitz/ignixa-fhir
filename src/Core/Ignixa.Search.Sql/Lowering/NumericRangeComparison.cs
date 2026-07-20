using Ignixa.Search.Extensions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the comparator-dependent predicate shared by Number and Quantity leaf lowering — both store
/// LowValue/HighValue with identical range semantics. Eq/Ne widen the value by the FHIR implied-decimal-
/// precision tolerance before comparing. :ap throws: it needs an additional relative tolerance this pure
/// function does not have.
/// </summary>
internal static class NumericRangeComparison
{
    public static Predicate Build(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, SearchComparator comparator, decimal value) => comparator switch
    {
        SearchComparator.Eq => BuildEq(context, lowColumn, highColumn, value),
        SearchComparator.Ne => BuildNe(context, lowColumn, highColumn, value),
        SearchComparator.Ge => new Predicate.GreaterThanOrEqual(lowColumn, context.Parameter(value)),
        SearchComparator.Gt or SearchComparator.Sa => new Predicate.GreaterThan(lowColumn, context.Parameter(value)),
        SearchComparator.Le => new Predicate.LessThanOrEqual(highColumn, context.Parameter(value)),
        SearchComparator.Lt or SearchComparator.Eb => new Predicate.LessThan(highColumn, context.Parameter(value)),
        SearchComparator.Ap => throw new NotSupportedException(
            "The :ap (approximately) comparator requires an additional relative tolerance this pure lowering " +
            "function doesn't have -- not implemented. Would need Lower.Run to accept an explicit widening policy."),
        _ => throw new NotSupportedException($"Unknown SearchComparator '{comparator}'."),
    };

    private static Predicate BuildEq(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, decimal value)
    {
        var modifier = value.GetPrescisionModifier();
        var lowerBound = context.Parameter(value - modifier);
        var upperBound = context.Parameter(value + modifier);
        return new Predicate.And(
            new Predicate.GreaterThanOrEqual(lowColumn, lowerBound),
            new Predicate.LessThanOrEqual(highColumn, upperBound));
    }

    private static Predicate BuildNe(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, decimal value)
    {
        var modifier = value.GetPrescisionModifier();
        var lowerBound = context.Parameter(value - modifier);
        var upperBound = context.Parameter(value + modifier);
        return new Predicate.Or(
            new Predicate.LessThan(highColumn, lowerBound),
            new Predicate.GreaterThan(lowColumn, upperBound));
    }
}
