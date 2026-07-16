namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// An index into QueryPlan.Ctes -- how one CteDefinition refers to another. Matches Explain()'s
/// cte0/cte1/... numbering by construction.
/// </summary>
public readonly record struct CteRef(int Index);
