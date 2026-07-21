namespace Ignixa.Search.Sql.Builders;

/// <summary>A labelled range of emitted SQL text — which characters a plan section produced.</summary>
/// <remarks>
/// <see cref="Label"/> says which section this is; <see cref="Kind"/> says what sort of section it is.
/// Both are needed: a consumer joins a range to its <see cref="Ast.PlanExplainRow"/> by label (via
/// <see cref="Ast.PlanExplainRow.CanonicalLabel"/>), but several ranges have no row bearing their label
/// and are rendered from <see cref="Kind"/> alone. See <see cref="SqlRangeKind"/>.
/// <para>
/// Labels are unique within one emitted statement — the three emit paths are mutually exclusive, so the
/// repeated <c>where</c> and <c>orderBy</c> sections never co-occur, and the rest are index-derived. That
/// is a property of <see cref="SqlBuilder"/> and of the range sequence, not of this type: a single range
/// cannot know what else was emitted, and nothing stops a future nested section from breaking it.
/// </para>
/// <para>
/// Not a positional record: <see cref="Start"/> and <see cref="Length"/> are buffer offsets and must be
/// non-negative, so construction validates rather than trusting every producer to get the arithmetic
/// right. The properties are get-only rather than <c>init</c> for the same reason — a <c>with</c>
/// expression copies through the compiler-generated copy constructor and would skip these checks
/// entirely, so the guard is only worth having if that route does not exist.
/// </para>
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
