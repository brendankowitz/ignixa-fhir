namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One line of an explained plan, kept as its two parts rather than the concatenated text
/// <see cref="PlanExplainer.Print"/> produces. A UI renders plan lines as selectable rows and joins each
/// back to its parameter through <see cref="Label"/> (<c>root</c>, <c>cte{i}</c>, <c>inc{i}</c>,
/// <c>sort</c>, <c>page</c>, <c>countOnly</c>), which it cannot do once the label has been glued to the
/// body with " = ". The flat string stays the golden-test format; this is the same content, unjoined.
/// </summary>
public sealed record PlanExplainRow(string Label, string Body);
