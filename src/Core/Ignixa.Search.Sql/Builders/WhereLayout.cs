namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// How a statement lays its WHERE clauses out. Both mean the same thing to SQL Server; the choice is
/// per-statement only because the emitted text is pinned by golden tests.
/// </summary>
internal enum WhereLayout
{
    /// <summary>One line, clauses joined by <c>" AND "</c>.</summary>
    Inline,

    /// <summary>One clause per line, continuations indented under the <c>WHERE</c>.</summary>
    Stacked,
}
