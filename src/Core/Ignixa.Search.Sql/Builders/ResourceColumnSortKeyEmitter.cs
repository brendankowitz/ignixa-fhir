using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Builders;

/// <summary>_lastUpdated and _type: the match set already carries the surrogate id and type id as columns, so
/// there is nothing to join and the value is that column. Never null, so the phase flag is irrelevant.</summary>
internal sealed class ResourceColumnSortKeyEmitter(string column) : SortKeyEmitter
{
    public override string? Join(SortKey key, int index, bool isPrimary) => null;

    public override string ValueExpr(SortKey key, int index, bool guaranteedNonNull) => column;
}
