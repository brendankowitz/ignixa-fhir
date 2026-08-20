using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Enforces the SQL compiler's own precondition for the <c>:ap</c> comparator — that a reference instant was
/// supplied — and defers the widening arithmetic itself to
/// <see cref="DateRangeComparisonSemantics.Widen"/>.
/// </summary>
/// <remarks>
/// The split is deliberate. The formula belongs to the prefix table, which every backend shares; the
/// precondition belongs here, because only this compiler can say where the instant was meant to come from.
/// Keeping the message in this layer is what lets it name <c>SearchSqlCompiler</c> and point at the actual
/// mistake rather than at arithmetic.
/// </remarks>
internal static class ApproximateDateRange
{
    /// <summary>
    /// Returns the reference instant, or throws explaining which component failed to supply it.
    /// </summary>
    /// <param name="referenceTime">The instant supplied by the lowering context, if any.</param>
    /// <returns>The reference instant.</returns>
    public static DateTimeOffset RequireReferenceTime(DateTimeOffset? referenceTime)
        => referenceTime ?? throw new InvalidOperationException(
            "The date ':ap' (approximately) comparator requires an explicit reference instant, but the " +
            "lowering context was constructed without one. SearchSqlCompiler supplies that instant from " +
            "its TimeProvider on every path, so reaching this state means the compiler was bypassed.");

    /// <summary>
    /// Computes the widened <c>:ap</c> endpoints, requiring a reference instant.
    /// </summary>
    /// <param name="value">The search value to widen.</param>
    /// <param name="referenceTime">The instant supplied by the lowering context, if any.</param>
    /// <returns>The widened endpoints.</returns>
    public static (DateTimeOffset Start, DateTimeOffset End) Widen(DateTimeSearchValue value, DateTimeOffset? referenceTime)
        => DateRangeComparisonSemantics.Widen(value, RequireReferenceTime(referenceTime));
}
