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
            // :ap is the one comparator with a defined meaning for a partial-precision value, so it is
            // handled before the exact-instant guard below rejects one.
            var (widenedStart, widenedEnd) = ApproximateDateRange.Widen(value, context.ApproximationReferenceTime);
            return new Predicate.And(
                new Predicate.GreaterThanOrEqual(column, context.Parameter(ToSurrogateId(widenedStart))),
                new Predicate.LessThanOrEqual(column, context.Parameter(ToSurrogateIdUpperBound(widenedEnd))));
        }

        if (value.Start != value.End)
        {
            throw new NotSupportedException(
                "_lastUpdated only supports an exact instant (Start == End) for now -- partial-precision " +
                "ranges need a point-column-vs-search-range comparator formula that has no live reference " +
                "implementation to verify against (the real pipeline's ProcessResourceLastUpdatedExpressionAsync " +
                "only ever compares against one already-resolved instant); deliberately deferred, not an oversight.");
        }

        // A single instant addresses a whole millisecond bucket [floor, floor + 79999], because the
        // database appends a uniquifier at write time. Every comparator is expressed against that closed
        // range; comparing against the bare floor would match only uniquifier 0.
        var lowerParam = context.Parameter(ToSurrogateId(value.Start));
        var upperParam = context.Parameter(ToSurrogateIdUpperBound(value.Start));

        return predicate.Comparator switch
        {
            SearchComparator.Eq => new Predicate.And(
                new Predicate.GreaterThanOrEqual(column, lowerParam),
                new Predicate.LessThanOrEqual(column, upperParam)),
            SearchComparator.Ne => new Predicate.Or(
                new Predicate.LessThan(column, lowerParam),
                new Predicate.GreaterThan(column, upperParam)),
            SearchComparator.Gt or SearchComparator.Sa => new Predicate.GreaterThan(column, upperParam),
            SearchComparator.Ge => new Predicate.GreaterThanOrEqual(column, lowerParam),
            SearchComparator.Lt or SearchComparator.Eb => new Predicate.LessThan(column, lowerParam),
            SearchComparator.Le => new Predicate.LessThanOrEqual(column, upperParam),
            _ => throw new NotSupportedException($"Unknown SearchComparator '{predicate.Comparator}'."),
        };
    }

    /// <summary>
    /// Largest uniquifier the database allocates within a single millisecond. dbo.ResourceSurrogateIdUniquifierSequence
    /// is declared MAXVALUE 79999, so every resource written in millisecond <c>m</c> occupies the closed
    /// range [ToSurrogateId(m), ToSurrogateId(m) + MaxUniquifier].
    /// </summary>
    private const long MaxUniquifier = 79999;

    /// <summary>
    /// Largest instant <see cref="ToSurrogateId"/> can encode without overflowing Int64, mirroring
    /// Ignixa.Domain.Abstractions.IdHelper.MaxDateTime.
    /// </summary>
    private static readonly DateTimeOffset MaxEncodableInstant =
        new DateTimeOffset(
            new DateTime(long.MaxValue >> 3, DateTimeKind.Utc).Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond,
            TimeSpan.Zero).AddTicks(-1);

    /// <summary>
    /// Converts an instant to the <em>inclusive lower</em> surrogate id for the millisecond containing it:
    /// millisecond-truncated UTC ticks, left-shifted 3 bits. Because ticks = milliseconds × 10000, that
    /// shift is arithmetically <c>msSince0001 × 80000</c> — the uniquifier is <em>not</em> held in the low
    /// three bits; it occupies the [0, 79999] value range above this floor. Transcribed from the data
    /// layer's IdHelper.ToId (pure math, no dependencies), since this Core project cannot reference it.
    /// </summary>
    /// <remarks>
    /// Instants past <see cref="MaxEncodableInstant"/> saturate rather than throw. A left shift never
    /// participates in <c>checked</c>, so an unguarded conversion would wrap negative and silently invert
    /// the comparison. Saturating is also the semantically correct answer: no stored resource can carry a
    /// lastUpdated beyond that instant, so clamping preserves the result set, and ApproximateDateRange
    /// deliberately saturates its widened endpoints for wide :ap ranges.
    /// </remarks>
    private static long ToSurrogateId(DateTimeOffset dateTimeOffset)
    {
        if (dateTimeOffset >= MaxEncodableInstant)
        {
            return long.MaxValue - MaxUniquifier;
        }

        var utc = dateTimeOffset.UtcDateTime;
        var truncatedTicks = utc.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond;
        return truncatedTicks << 3;
    }

    /// <summary>
    /// The inclusive lower-bound surrogate id for <paramref name="instant"/> — the same floor
    /// <see cref="ToSurrogateId"/> computes for a <c>_lastUpdated ge</c> comparison. Exposed for
    /// <c>$everything</c>'s <c>_since</c>, which bounds compartment members on
    /// <c>meta.lastUpdated &gt;= _since</c> exactly as a <c>_lastUpdated ge _since</c> search would, so both
    /// paths agree on the boundary millisecond rather than each transcribing the shift independently.
    /// </summary>
    internal static long ToInclusiveLowerSurrogateId(DateTimeOffset instant) => ToSurrogateId(instant);

    /// <summary>
    /// Converts an instant to the <em>inclusive upper</em> surrogate id for the millisecond containing it.
    /// Comparing against the bare floor would match only the single resource that happened to draw
    /// uniquifier 0, silently dropping up to 79,999 rows per boundary millisecond. The upstream
    /// GetResourcesByTypeAndSurrogateIdRange procedure applies the same <c>+ 79999</c> widening.
    /// </summary>
    private static long ToSurrogateIdUpperBound(DateTimeOffset dateTimeOffset)
        => ToSurrogateId(dateTimeOffset) + MaxUniquifier;
}
