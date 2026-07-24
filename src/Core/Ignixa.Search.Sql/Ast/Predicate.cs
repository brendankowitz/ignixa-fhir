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

    /// <summary>
    /// Logical negation of a predicate, emitted as <c>NOT (…)</c>. Used for a negated resource-column
    /// filter (<c>_id:not=a,b</c> → <c>NOT (ResourceId = @p0 OR ResourceId = @p1)</c>) in the outer WHERE,
    /// where the negation must be visible rather than silently dropped.
    /// </summary>
    public sealed record Not(Predicate Operand) : Predicate;

    public sealed record IsNull(SqlColumnRef Column) : Predicate;

    /// <summary>
    /// A predicate that can never hold, emitted as <c>1 = 0</c>. <paramref name="Reason"/> names the value
    /// that made it unsatisfiable (e.g. an unresolvable token system) so the trace can report a known miss
    /// instead of leaving that fact discoverable only by reading the emitted SQL. It never affects emission.
    /// </summary>
    public sealed record False(string? Reason = null) : Predicate;

    public sealed record PrefixOfParameter(SqlColumnRef Column, SqlParameterRef Value, string? Collation = null) : Predicate;
}
