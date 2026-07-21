namespace Ignixa.Search.Sql.Builders;

/// <summary>A labelled range of emitted SQL text — which characters a plan section produced.</summary>
/// <remarks>
/// Not a positional record: <see cref="Start"/> and <see cref="Length"/> are buffer offsets and must be
/// non-negative, so construction validates rather than trusting every producer to get the arithmetic
/// right.
/// </remarks>
public sealed record SqlTextRange
{
    public SqlTextRange(string label, int start, int length)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Label = label;
        Start = start;
        Length = length;
    }

    public string Label { get; init; }

    public int Start { get; init; }

    public int Length { get; init; }

    public void Deconstruct(out string label, out int start, out int length)
    {
        label = Label;
        start = Start;
        length = Length;
    }
}
