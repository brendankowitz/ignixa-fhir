using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Builders;

/// <summary>A sort over an aggregate of a repeating search parameter: joins a MIN/MAX-per-resource derived
/// table rather than the IsMin/IsMax flag rows the scalar kinds use.</summary>
internal sealed class AggregatedSortKeyEmitter : SortKeyEmitter
{
    public override string? Join(SortKey key, int index, bool isPrimary)
    {
        // Key 0 in the Valued phase must gate on the key being present (INNER), like SearchParamSortKeyEmitter
        // (String/Date): an unconditional LEFT would leak missing-key rows across the phase boundary and let a
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
