#pragma warning disable CA1716

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// A WHERE-clause fragment over a single table's columns; it never spans tables. Multiple column
/// comparisons (e.g. a composite's two slots) are expressed as nested <see cref="And"/>.
/// </summary>
public abstract record Predicate
{
    public sealed record Equal(SqlColumnRef Column, SqlParameterRef Value, string? Collation = null) : Predicate;

    public sealed record Like(SqlColumnRef Column, SqlParameterRef Value, LikeMatch Match, string? Collation = null) : Predicate;

    public sealed record And(Predicate Left, Predicate Right) : Predicate;

    public sealed record LessThan(SqlColumnRef Column, SqlParameterRef Value) : Predicate;

    public sealed record LessThanOrEqual(SqlColumnRef Column, SqlParameterRef Value) : Predicate;

    public sealed record GreaterThan(SqlColumnRef Column, SqlParameterRef Value) : Predicate;

    public sealed record GreaterThanOrEqual(SqlColumnRef Column, SqlParameterRef Value) : Predicate;

    public sealed record Or(Predicate Left, Predicate Right) : Predicate;

    public sealed record IsNull(SqlColumnRef Column) : Predicate;

    public sealed record False : Predicate;

    public sealed record PrefixOfParameter(SqlColumnRef Column, SqlParameterRef Value, string? Collation = null) : Predicate;
}
