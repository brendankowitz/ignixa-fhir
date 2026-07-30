using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers _id/_type/_lastUpdated — resource-column search parameters that target dbo.Resource's own
/// columns via QueryPlan.OuterPredicate rather than a ParamSource table. Returns null for any other code,
/// which Lower's extraction pass reads as "not a resource-column predicate, dispatch it normally."
/// </summary>
internal static class ResourceColumnLoweringRule
{
    /// <summary>
    /// True for the parameter codes this rule handles. These target dbo.Resource's own columns, so they
    /// never need a SearchParamId — callers that resolve or dispatch by SearchParamId must skip them.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="IntrinsicSearchParameters"/> so that this rule, the indexer that skips these
    /// codes, and any host outside this assembly cannot disagree about the set. The alias is kept because
    /// "resource column" is the right word at the lowering call sites, which are about <em>this rule's</em>
    /// applicability to dbo.Resource rather than about the storage-agnostic classification.
    /// </remarks>
    public static bool IsResourceColumnCode(string parameterCode)
        => IntrinsicSearchParameters.IsIntrinsicCode(parameterCode);

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

        if (context.UnmatchableResourceType(value.Code) is { } unmatchable)
        {
            return unmatchable;
        }

        var table = SqlCatalog.Default.Table("Resource");
        return new Predicate.Equal(new SqlColumnRef(table.TableName, "ResourceTypeId"), context.Parameter(context.ResourceTypeId(value.Code)));
    }

    /// <summary>
    /// Only plain equality is implemented; a modifier (notably ":not", which silently dropped would produce
    /// a positive match instead of a negation) or a non-Eq comparator needs semantics this rule lacks, so
    /// throw rather than ignore.
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

        // The search value is a closed range [Start, End] encoding FHIR partial-date precision; a stored row
        // is a single point (ResourceSurrogateId = one ms plus a write-time uniquifier), so this is a
        // point-vs-range comparison. Each endpoint widens to its whole millisecond bucket — floor to [ms],
        // ceiling to [ms + MaxUniquifier] — because a bare floor would match only uniquifier 0.
        var lowerParam = context.Parameter(ToSurrogateId(value.Start));
        var upperParam = context.Parameter(ToSurrogateIdUpperBound(value.End));

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
    /// Largest uniquifier the database allocates within a single millisecond
    /// (dbo.ResourceSurrogateIdUniquifierSequence MAXVALUE 79999), so a resource written in millisecond
    /// <c>m</c> occupies the closed range [ToSurrogateId(m), ToSurrogateId(m) + MaxUniquifier].
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
    /// Converts an instant to the <em>inclusive lower</em> surrogate id for its millisecond: ms-truncated UTC
    /// ticks left-shifted 3 bits (= <c>msSince0001 × 80000</c>; uniquifier occupies [0, 79999] above). Instants
    /// past <see cref="MaxEncodableInstant"/> saturate on input to stay monotonic, since an unchecked shift would
    /// wrap negative and invert the comparison.
    /// </summary>
    private static long ToSurrogateId(DateTimeOffset dateTimeOffset)
    {
        var clamped = dateTimeOffset >= MaxEncodableInstant ? MaxEncodableInstant : dateTimeOffset;
        var truncatedTicks = clamped.UtcDateTime.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond;
        return truncatedTicks << 3;
    }

    /// <summary>
    /// Converts an instant to the <em>inclusive upper</em> surrogate id for its millisecond. Comparing
    /// against the bare floor would match only uniquifier 0, dropping up to 79,999 rows per boundary ms; the
    /// upstream GetResourcesByTypeAndSurrogateIdRange procedure applies the same <c>+ 79999</c> widening.
    /// </summary>
    private static long ToSurrogateIdUpperBound(DateTimeOffset dateTimeOffset)
        => ToSurrogateId(dateTimeOffset) + MaxUniquifier;
}
