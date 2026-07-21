namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One line of an explained plan, kept as its parts rather than the concatenated text
/// <see cref="PlanExplainer.Print"/> produces. A UI renders plan lines as selectable rows and joins each
/// back to its parameter and its SQL text, which it cannot do once the label has been glued to the body
/// with " = ". The flat string stays the golden-test format; this is the same content, unjoined.
/// </summary>
/// <remarks>
/// <see cref="Label"/> is the display name (<c>root</c>, <c>cte{i}</c>, <c>inc{i}</c>, <c>sort</c>,
/// <c>page</c>, <c>countOnly</c>). <see cref="CanonicalLabel"/> is the identifier that same row carries in
/// the emitted SQL and in <see cref="Tracing.CteProvenance"/>. The two differ for exactly one row: the
/// match CTE prints as <c>root</c> for readability but is emitted as
/// <see cref="Builders.SqlLabels.CteLabel"/> of <c>plan.Match.Index</c>. Join on
/// <see cref="CanonicalLabel"/>, never on <see cref="Label"/> — the latter is display text and addresses
/// nothing.
/// <para>
/// <see cref="Kind"/> and <see cref="ReferencedCteIndexes"/> are the two things <see cref="Body"/> used to
/// be the only carrier of. Both are read off the plan node directly, so a consumer never has to
/// prefix-match formatted prose for the node's case or regex it for <c>cte(\d+)</c> to find which CTEs a
/// structural node composes. <see cref="Body"/> is display text and is free to change wording.
/// </para>
/// <para>
/// Rows with no SQL of their own (<c>sort</c>, <c>page</c>, <c>countOnly</c>) repeat their display name as
/// the canonical one, so a consumer never has to special-case a null.
/// </para>
/// </remarks>
/// <param name="Label">Display name for the row.</param>
/// <param name="CanonicalLabel">The identifier this row is addressable by in the emitted SQL.</param>
/// <param name="Kind">Which plan node produced the row — see <see cref="PlanRowKind"/>.</param>
/// <param name="Body">Formatted, human-facing description. Not a stable contract.</param>
/// <param name="ReferencedCteIndexes">
/// Indexes of the CTEs this row's node composes, in the order the node names them. Empty for leaf
/// sources and for non-CTE rows.
/// </param>
public sealed record PlanExplainRow(
    string Label,
    string CanonicalLabel,
    string Kind,
    string Body,
    IReadOnlyList<int> ReferencedCteIndexes);
