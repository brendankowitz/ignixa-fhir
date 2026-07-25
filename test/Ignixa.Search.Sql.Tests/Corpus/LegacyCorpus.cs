using System.IO;
using System.Text.Json;

namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>Loads the checked-in legacy-SQL corpus from the test output directory.</summary>
public static class LegacyCorpus
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<IReadOnlyList<CorpusEntry>> Cached = new(Load);

    public static IReadOnlyList<CorpusEntry> Entries => Cached.Value;

    private static IReadOnlyList<CorpusEntry> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "legacy-sql-corpus.json");
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<CorpusDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException($"corpus at {path} deserialized to null");
        return document.Entries;
    }

    private sealed record CorpusDocument(string CaptureRunId, int EntryCount, IReadOnlyList<CorpusEntry> Entries);
}
