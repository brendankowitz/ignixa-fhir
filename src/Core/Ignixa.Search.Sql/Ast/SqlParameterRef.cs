namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// A placeholder for one user-supplied value. Emit turns it into a bound SQL parameter, so emitted SQL
/// text never contains a literal user value — the invariant that keeps the compiler injection-safe.
/// </summary>
public sealed record SqlParameterRef(object Value);
