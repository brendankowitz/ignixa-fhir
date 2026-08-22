using System.Text.Json;
using Ignixa.Search.Indexing;
using Ignixa.Search.Serialization;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal static class SearchIndexCanonicalizer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new CompactSearchIndexConverter() }
    };

    public static IReadOnlyList<string> Canonicalize(IEnumerable<SearchIndexEntry> entries) =>
        entries.Select(entry =>
                $"{entry.SearchParameter.Url}|{JsonSerializer.Serialize(new List<SearchIndexEntry> { entry }, Options)}")
            .Order(StringComparer.Ordinal)
            .ToArray();
}
