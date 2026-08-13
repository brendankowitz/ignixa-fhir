using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// Renders the per-kind half of a sort key: the join that brings its value into scope, and the expression that
/// reads that value. <see cref="SortEmitter"/> keeps the policy that is not per-kind — which keys are active in
/// the current phase, which one is primary, and whether a key is guaranteed non-null — and passes the answers in.
/// One implementation per <see cref="SortKeyKind"/>, so adding a kind is a new class plus a
/// <see cref="For(SortKeyKind)"/> arm rather than a coordinated edit to parallel switches.
/// </summary>
internal abstract class SortKeyEmitter
{
    /// <summary>The join bringing this key's value into scope, or null when the match set already projects it.
    /// <paramref name="isPrimary"/> selects INNER over LEFT: the primary key gates the row set, tie-breakers
    /// must not drop rows.</summary>
    public abstract string? Join(SortKey key, int index, bool isPrimary);

    /// <summary>The expression reading this key's sort value. <paramref name="guaranteedNonNull"/> is true only
    /// for the primary key of a Valued phase, where the INNER join already excludes missing values and the
    /// ISNULL wrapper would be dead weight.</summary>
    public abstract string ValueExpr(SortKey key, int index, bool guaranteedNonNull);

    /// <summary>Resolves the emitter for a kind. The default arm is reachable: C# does not check enum switch
    /// exhaustiveness, and <see cref="SortKeyKind"/> is public, so a caller building a QueryPlan directly can cast
    /// an undefined value. This deliberately throws where the previous if-chains fell through to Date and emitted a
    /// silently wrong DateTimeSearchParam query -- matching how this codebase already treats undefined
    /// ChainDirection and IncludeDirection values.</summary>
    public static SortKeyEmitter For(SortKeyKind kind) => kind switch
    {
        SortKeyKind.LastUpdated => LastUpdated,
        SortKeyKind.ResourceType => ResourceType,
        SortKeyKind.ResourceId => ResourceId,
        SortKeyKind.Aggregated => Aggregated,
        SortKeyKind.String => String,
        SortKeyKind.Date => Date,
        _ => throw new NotSupportedException(
            $"No SortKeyEmitter registered for SortKeyKind '{kind}' -- add one alongside the existing kinds."),
    };

    private static readonly SortKeyEmitter LastUpdated = new ResourceColumnSortKeyEmitter("m.Sid1");
    private static readonly SortKeyEmitter ResourceType = new ResourceColumnSortKeyEmitter("m.T1");
    private static readonly SortKeyEmitter ResourceId = new ResourceIdSortKeyEmitter();
    private static readonly SortKeyEmitter Aggregated = new AggregatedSortKeyEmitter();
    private static readonly SortKeyEmitter String = new SearchParamSortKeyEmitter("StringSearchParam", "Text", "N''");
    private static readonly SortKeyEmitter Date = new SearchParamSortKeyEmitter("DateTimeSearchParam", "StartDateTime", "'0001-01-01T00:00:00.0000000'");
}
