namespace Ignixa.Search.Sql.Ast;

/// <summary>A reference to one table column that a <see cref="Predicate"/> compares against.</summary>
public sealed record SqlColumnRef(string Table, string Column);
