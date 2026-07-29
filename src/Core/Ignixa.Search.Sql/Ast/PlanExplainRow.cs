namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One line of an explained plan, kept as its parts rather than the concatenated text
/// <see cref="PlanExplainer.Print"/> produces — a UI renders plan lines as selectable rows and joins each
/// back to its parameter and SQL text, which it cannot do once label and body are glued with " = ".
/// </summary>
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

    /// <summary>The identifier this row is addressable by in the emitted SQL. Differs from <see cref="Label"/>
    /// only for the match CTE, which displays as <c>root</c> but is emitted as <c>cte{Match.Index}</c>. Join on
    /// this, never on <see cref="Label"/>.</summary>
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
