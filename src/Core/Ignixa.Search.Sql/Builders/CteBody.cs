namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// One CTE's inner SELECT, rendered but not yet named. Carrying the text and its optional ranges
/// separately lets <see cref="CteEmitter.WriteCteHeader"/> own the "name AS (...)" wrapping and the
/// section bookkeeping for every CTE kind uniformly, instead of each emitter writing its own header.
/// </summary>
internal sealed record CteBody(string Text, IReadOnlyList<SqlTextRange>? Ranges = null);
