#pragma warning disable CA1716

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// A WHERE-clause fragment over one ParamSource CTE's own table -- never spans tables. Composites
/// (out of scope for this plan) would express multiple column comparisons as nested And.
/// </summary>
public abstract record Predicate
{
    public sealed record Equal(SqlColumnRef Column, SqlParameterRef Value, string? Collation = null) : Predicate;

    public sealed record Like(SqlColumnRef Column, SqlParameterRef Value, LikeMatch Match, string? Collation = null) : Predicate;

    public sealed record And(Predicate Left, Predicate Right) : Predicate;
}
