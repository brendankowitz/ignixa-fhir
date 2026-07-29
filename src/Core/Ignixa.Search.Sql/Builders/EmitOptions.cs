namespace Ignixa.Search.Sql.Builders;

/// <summary>Emission options. Text ranges are opt-in because they allocate per call.</summary>
internal sealed record EmitOptions(bool IncludeTextRanges);
