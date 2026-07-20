using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>Links one CTE to the IR node that produced it. Holds the node, not a span: the plan is
/// per-search, so a bare span would be ambiguous across repeated parameters.</summary>
public sealed record CteOrigin(int CteIndex, Expression SourceNode);
