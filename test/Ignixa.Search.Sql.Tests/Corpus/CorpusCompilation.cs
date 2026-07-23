namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>The outcome of putting one captured URL through the compiler.</summary>
public sealed record CorpusCompilation(CorpusEntry Entry, string? Sql, string? FailureStage, string? FailureMessage)
{
    public bool Succeeded => Sql is not null;

    public static CorpusCompilation Compiled(CorpusEntry entry, string sql) => new(entry, sql, null, null);

    public static CorpusCompilation Failed(CorpusEntry entry, string stage, string message) => new(entry, null, stage, message);
}
