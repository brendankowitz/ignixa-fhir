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

/// <summary>_lastUpdated and _type: the match set already carries the surrogate id and type id as columns, so
/// there is nothing to join and the value is that column. Never null, so the phase flag is irrelevant.</summary>
internal sealed class ResourceColumnSortKeyEmitter(string column) : SortKeyEmitter
{
    public override string? Join(SortKey key, int index, bool isPrimary) => null;

    public override string ValueExpr(SortKey key, int index, bool guaranteedNonNull) => column;
}

/// <summary>_id: joins dbo.Resource to reach the business id.</summary>
internal sealed class ResourceIdSortKeyEmitter : SortKeyEmitter
{
    public override string? Join(SortKey key, int index, bool isPrimary)
        => $"\n{(isPrimary ? "INNER" : "LEFT")} JOIN dbo.Resource rid{index} ON rid{index}.ResourceTypeId = m.T1 AND rid{index}.ResourceSurrogateId = m.Sid1";

    // Unwrapped even as a LEFT-joined secondary key: (ResourceTypeId, ResourceSurrogateId) is
    // dbo.Resource's clustered PK, so every (T1, Sid1) has a matching row and the LEFT never yields
    // NULL. Architectural, not FK-enforced — a match source of non-resource rows would break it.
    public override string ValueExpr(SortKey key, int index, bool guaranteedNonNull) => $"rid{index}.ResourceId";
}

/// <summary>A sort over an aggregate of a repeating search parameter: joins a MIN/MAX-per-resource derived
/// table rather than the IsMin/IsMax flag rows the scalar kinds use.</summary>
internal sealed class AggregatedSortKeyEmitter : SortKeyEmitter
{
    public override string? Join(SortKey key, int index, bool isPrimary)
    {
        // Key 0 in the Valued phase must gate on the key being present (INNER), like String/Date
        // below: an unconditional LEFT would leak missing-key rows across the phase boundary and let a
        // NULL AggValue reach the seek unwrapped. INNER is safe — MIN/MAX over zero rows yields no
        // output row, exactly INNER's semantics.
        var aggFunc = key.Direction == SortOrder.Ascending ? "MIN" : "MAX";
        return $"\n{(isPrimary ? "INNER" : "LEFT")} JOIN (\n" +
            $"    SELECT ResourceTypeId, ResourceSurrogateId, {aggFunc}({key.Column!.Name}) AS AggValue\n" +
            $"    FROM {key.Table!.SchemaName}.{key.Table.TableName}\n" +
            $"    WHERE SearchParamId = {key.SearchParamId}\n" +
            $"    GROUP BY ResourceTypeId, ResourceSurrogateId\n" +
            $") sk{index} ON sk{index}.ResourceTypeId = m.T1 AND sk{index}.ResourceSurrogateId = m.Sid1";
    }

    public override string ValueExpr(SortKey key, int index, bool guaranteedNonNull)
    {
        var aggRaw = $"sk{index}.AggValue";
        return guaranteedNonNull ? aggRaw : $"ISNULL({aggRaw}, {SentinelFor(key.Column!.SqlType)})";
    }

    /// <summary>
    /// Maps a search-param column's DDL SQL type to the literal ISNULL substitutes for a missing aggregated
    /// sort value. Aggregated leaf types resolve to varchar (Token/Reference/Uri) or decimal (Number/Quantity);
    /// nvarchar is included for parity with String's N'' sentinel though no Aggregated column uses it.
    /// </summary>
    private static string SentinelFor(string sqlType) => sqlType switch
    {
        "varchar" => "''",
        "nvarchar" => "N''",
        "decimal" or "numeric" or "int" or "bigint" or "smallint" or "float" or "money" => "0",
        _ => throw new NotSupportedException(
            $"No ISNULL sentinel defined for aggregated sort SqlType '{sqlType}' -- add one to SentinelFor " +
            "after confirming the real DDL column type, matching the varchar/decimal families already handled."),
    };
}

/// <summary>String and Date keys: structurally identical, differing only in the search-param table, the value
/// column, and the sentinel substituted for a missing value.</summary>
internal sealed class SearchParamSortKeyEmitter(string table, string column, string sentinel) : SortKeyEmitter
{
    public string Table => table;

    public override string? Join(SortKey key, int index, bool isPrimary)
        => $"\n{(isPrimary ? "INNER" : "LEFT")} JOIN dbo.{table} sk{index}\n" +
            $"    ON sk{index}.ResourceTypeId = m.T1 AND sk{index}.ResourceSurrogateId = m.Sid1\n" +
            $"   AND sk{index}.SearchParamId = {key.SearchParamId} AND sk{index}.{(key.Direction == SortOrder.Ascending ? "IsMin" : "IsMax")} = 1";

    public override string ValueExpr(SortKey key, int index, bool guaranteedNonNull)
    {
        var raw = $"sk{index}.{column}";
        return guaranteedNonNull ? raw : $"ISNULL({raw}, {sentinel})";
    }
}
