using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers _id/_type/_lastUpdated — resource-column search parameters that target dbo.Resource's own
/// columns via QueryPlan.OuterPredicate rather than a ParamSource table. Returns null for any other
/// parameter code, which the caller (Lower's extraction pass) reads as "not a resource-column predicate,
/// dispatch it normally."
/// </summary>
public static class ResourceColumnLoweringRule
{
    /// <summary>
    /// True for the parameter codes this rule handles. These target dbo.Resource's own columns, so they
    /// never need a SearchParamId — callers that resolve or dispatch by SearchParamId must skip them.
    /// </summary>
    public static bool IsResourceColumnCode(string parameterCode)
        => parameterCode is "_id" or "_type" or "_lastUpdated";

    public static Predicate? TryLower(SearchParameterPredicateExpression predicate, LeafContext context) => predicate.Parameter.Code switch
    {
        "_id" => IdEquals(predicate, context),
        "_type" => TypeEquals(predicate, context),
        "_lastUpdated" => LastUpdatedCompare(predicate, context),
        _ => null,
    };

    private static Predicate IdEquals(SearchParameterPredicateExpression predicate, LeafContext context)
    {
        RequireNoModifierOrComparator(predicate, "_id");
        var value = (TokenSearchValue)predicate.Value;
        if (value.System is not null)
        {
            throw new NotSupportedException("_id does not support a System qualifier.");
        }

        if (string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException("_id requires a non-empty value.");
        }

        var table = SqlCatalog.Default.Table("Resource");
        return new Predicate.Equal(new SqlColumnRef(table.TableName, "ResourceId"), context.Parameter(value.Code));
    }

    private static Predicate TypeEquals(SearchParameterPredicateExpression predicate, LeafContext context)
    {
        RequireNoModifierOrComparator(predicate, "_type");
        var value = (TokenSearchValue)predicate.Value;
        if (value.System is not null)
        {
            throw new NotSupportedException("_type does not support a System qualifier.");
        }

        if (string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException("_type requires a non-empty resource type name.");
        }

        var table = SqlCatalog.Default.Table("Resource");
        return new Predicate.Equal(new SqlColumnRef(table.TableName, "ResourceTypeId"), context.Parameter(context.ResourceTypeId(value.Code)));
    }

    /// <summary>
    /// This rule only implements plain equality -- a modifier (most importantly ":not", which would
    /// otherwise be silently dropped here and produce a positive match instead of a negation, exactly
    /// the bug Lower's own :not handling exists to prevent) or a non-Eq comparator would need semantics
    /// this rule doesn't have. Throwing rather than silently ignoring either.
    /// </summary>
    private static void RequireNoModifierOrComparator(SearchParameterPredicateExpression predicate, string code)
    {
        if (predicate.Modifier is not null)
        {
            throw new NotSupportedException($"{code} does not support the ':{predicate.Modifier.SearchModifierCode}' modifier yet.");
        }

        if (predicate.Comparator != SearchComparator.Eq)
        {
            throw new NotSupportedException($"{code} only supports the 'eq' comparator, not '{predicate.Comparator}'.");
        }
    }

    private static Predicate LastUpdatedCompare(SearchParameterPredicateExpression predicate, LeafContext context)
    {
        if (predicate.Modifier is not null)
        {
            throw new NotSupportedException($"_lastUpdated does not support the ':{predicate.Modifier.SearchModifierCode}' modifier yet.");
        }

        var value = (DateTimeSearchValue)predicate.Value;
        var table = SqlCatalog.Default.Table("Resource");
        var column = new SqlColumnRef(table.TableName, "ResourceSurrogateId");

        if (predicate.Comparator == SearchComparator.Ap)
        {
            // :ap is the one comparator that has a defined meaning for a partial-precision value --
            // ApproximateDateRange.Widen (the same pure helper the date leaf/composite lowering uses)
            // widens [Start, End] by the FHIR-recommended tolerance around context's fixed reference
            // instant, and both widened endpoints are compared against the single ResourceSurrogateId
            // point column via the same ToSurrogateId conversion the other comparators use below.
            var (widenedStart, widenedEnd) = ApproximateDateRange.Widen(value, context.ApproximationReferenceTime);
            return new Predicate.And(
                new Predicate.GreaterThanOrEqual(column, context.Parameter(ToSurrogateId(widenedStart))),
                new Predicate.LessThanOrEqual(column, context.Parameter(ToSurrogateId(widenedEnd))));
        }

        if (value.Start != value.End)
        {
            throw new NotSupportedException(
                "_lastUpdated only supports an exact instant (Start == End) for now -- partial-precision " +
                "ranges need a point-column-vs-search-range comparator formula that has no live reference " +
                "implementation to verify against (the real pipeline's ProcessResourceLastUpdatedExpressionAsync " +
                "only ever compares against one already-resolved instant); deliberately deferred, not an oversight.");
        }

        var targetId = ToSurrogateId(value.Start);
        var targetParam = context.Parameter(targetId);

        return predicate.Comparator switch
        {
            SearchComparator.Eq => new Predicate.Equal(column, targetParam),
            SearchComparator.Ne => new Predicate.Or(new Predicate.LessThan(column, targetParam), new Predicate.GreaterThan(column, targetParam)),
            SearchComparator.Gt or SearchComparator.Sa => new Predicate.GreaterThan(column, targetParam),
            SearchComparator.Ge => new Predicate.GreaterThanOrEqual(column, targetParam),
            SearchComparator.Lt or SearchComparator.Eb => new Predicate.LessThan(column, targetParam),
            SearchComparator.Le => new Predicate.LessThanOrEqual(column, targetParam),
            _ => throw new NotSupportedException($"Unknown SearchComparator '{predicate.Comparator}'."),
        };
    }

    /// <summary>
    /// Converts an instant to the surrogate id used by ResourceSurrogateId. Millisecond-truncated UTC
    /// ticks, left-shifted 3 bits — the low 3 bits hold a per-millisecond uniquifier the database
    /// allocates at write time, which a search-time comparison does not need. Transcribed from the data
    /// layer's IdHelper.ToId (pure math, no dependencies), since this Core project cannot reference it.
    /// </summary>
    private static long ToSurrogateId(DateTimeOffset dateTimeOffset)
    {
        var utc = dateTimeOffset.UtcDateTime;
        var truncatedTicks = utc.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond;
        return truncatedTicks << 3;
    }
}
