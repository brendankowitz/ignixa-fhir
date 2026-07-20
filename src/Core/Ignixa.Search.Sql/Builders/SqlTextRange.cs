namespace Ignixa.Search.Sql.Builders;

/// <summary>A labelled range of emitted SQL text — which characters a plan section produced.</summary>
public sealed record SqlTextRange(string Label, int Start, int Length);
