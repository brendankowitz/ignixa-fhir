using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Builders;

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
