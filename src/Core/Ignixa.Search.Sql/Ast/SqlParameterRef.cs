namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// A placeholder for one user-supplied value. Emit turns this into a real parameterized SQL parameter
/// -- SQL text never contains a literal user value (design doc's "no unparameterized user value"
/// AST invariant).
/// </summary>
public sealed record SqlParameterRef(object Value);
