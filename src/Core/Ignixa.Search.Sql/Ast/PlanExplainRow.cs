namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One line of an explained plan, kept as its parts rather than the concatenated text
/// <see cref="PlanExplainer.Print"/> produces. A UI renders plan lines as selectable rows and joins each
/// back to its parameter and its SQL text, which it cannot do once the label has been glued to the body
/// with " = ". The flat string stays the golden-test format; this is the same content, unjoined.
/// </summary>
/// <remarks>
/// <see cref="Label"/> is the display name (<c>root</c>, <c>cte{i}</c>, <c>includeBoundary</c>,
/// <c>inc{i}</c>, <c>sort</c>, <c>page</c>, <c>countOnly</c>). <see cref="CanonicalLabel"/> is the identifier that same row carries in
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
/// Every row repeats its display name as the canonical one except the match CTE, so a consumer never has
/// to special-case a null. Note that <c>sort</c>, <c>page</c> and <c>countOnly</c> do affect the emitted
/// SQL — they contribute ORDER BY, TOP, seek predicates and the COUNT_BIG shape — they simply own no
/// <see cref="Builders.SqlTextRange"/> carrying their name, so looking one up by their label finds
/// nothing.
/// </para>
/// <para>
/// Not a positional record, for the same reason as its siblings: the labels and kind are identifiers a
/// consumer addresses rows by, and an empty one addresses nothing. <see cref="ReferencedCteIndexes"/> is
/// copied on the way in and compared element-wise by <see cref="Equals(PlanExplainRow)"/> — a record's
/// synthesized equality compares a collection property by reference, which would make two rows describing
/// the same plan node unequal.
/// </para>
/// </remarks>
public sealed record PlanExplainRow
{
    public PlanExplainRow(
        string label,
        string canonicalLabel,
        string kind,
        string body,
        IReadOnlyList<int> referencedCteIndexes)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentException.ThrowIfNullOrEmpty(canonicalLabel);
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(referencedCteIndexes);

        foreach (var index in referencedCteIndexes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(referencedCteIndexes));
        }

        Label = label;
        CanonicalLabel = canonicalLabel;
        Kind = kind;
        Body = body;
        ReferencedCteIndexes = [.. referencedCteIndexes];
    }

    /// <summary>Display name for the row.</summary>
    public string Label { get; }

    /// <summary>The identifier this row is addressable by in the emitted SQL.</summary>
    public string CanonicalLabel { get; }

    /// <summary>Which plan node produced the row — see <see cref="PlanRowKind"/>.</summary>
    public string Kind { get; }

    /// <summary>Formatted, human-facing description. Not a stable contract.</summary>
    public string Body { get; }

    /// <summary>
    /// Indexes of the CTEs this row's node composes, in the order the node names them — the order is
    /// load-bearing for <see cref="PlanRowKind.Except"/>, where left and right are not interchangeable.
    /// Empty for leaf sources and for non-CTE rows.
    /// </summary>
    public IReadOnlyList<int> ReferencedCteIndexes { get; }

    public bool Equals(PlanExplainRow? other)
        => other is not null
            && Label == other.Label
            && CanonicalLabel == other.CanonicalLabel
            && Kind == other.Kind
            && Body == other.Body
            && ReferencedCteIndexes.SequenceEqual(other.ReferencedCteIndexes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Label);
        hash.Add(CanonicalLabel);
        hash.Add(Kind);
        hash.Add(Body);
        foreach (var index in ReferencedCteIndexes)
        {
            hash.Add(index);
        }

        return hash.ToHashCode();
    }

    public void Deconstruct(
        out string label,
        out string canonicalLabel,
        out string kind,
        out string body,
        out IReadOnlyList<int> referencedCteIndexes)
    {
        label = Label;
        canonicalLabel = CanonicalLabel;
        kind = Kind;
        body = Body;
        referencedCteIndexes = ReferencedCteIndexes;
    }
}
