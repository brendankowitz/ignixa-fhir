using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Builders;

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
