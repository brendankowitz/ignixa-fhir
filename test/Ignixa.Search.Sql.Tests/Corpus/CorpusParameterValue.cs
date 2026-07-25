namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// A bound parameter the legacy engine passed with a captured query, recovered from the
/// `DECLARE @p0 varchar(64) = '...'` preamble the engine's own LogEvent trace writes.
/// </summary>
public sealed record CorpusParameterValue(string Type, string Value);
