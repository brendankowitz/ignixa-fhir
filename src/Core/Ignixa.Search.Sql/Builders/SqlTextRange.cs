namespace Ignixa.Search.Sql.Builders;

/// <summary>A labelled range of emitted SQL text — which characters a plan section produced.</summary>
/// <remarks>
/// <see cref="Label"/> is which section, <see cref="Kind"/> is what sort. Both matter: consumers join a range
/// to its <see cref="Ast.PlanExplainRow"/> by label, but several ranges have no such row and render from
/// <see cref="Kind"/> alone. See <see cref="SqlRangeKind"/>.
/// </remarks>
public sealed record SqlTextRange
{
    public SqlTextRange(string label, string kind, int start, int length)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Label = label;
        Kind = kind;
        Start = start;
        Length = length;
    }

    public string Label { get; }

    public string Kind { get; }

    public int Start { get; }

    public int Length { get; }

    public void Deconstruct(out string label, out string kind, out int start, out int length)
    {
        label = Label;
        kind = Kind;
        start = Start;
        length = Length;
    }
}
