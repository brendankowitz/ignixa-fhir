using System.Text.Json.Serialization;

namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// One captured search: the request URL a TestScript issued and the SQL the shipping search engine
/// executed for it. Extracted from an ignixa-sql-capture artifact by Corpus/tools/extract-corpus.py.
/// </summary>
public sealed record CorpusEntry(
    string Url,
    string LegacySql,
    IReadOnlyDictionary<string, string> ParameterTypes,
    IReadOnlyDictionary<string, CorpusParameterValue> ParameterValues,
    IReadOnlyList<string> SourceScripts,
    [property: JsonPropertyName("corroboratingEvents")] int CorroboratingEvents,
    [property: JsonPropertyName("rejectedVariants")] int RejectedVariants)
{
    /// <summary>The resource type the search targets, taken from the URL path.</summary>
    public string ResourceType => Url.TrimStart('/').Split('/', '?')[0];

    /// <summary>The raw query string, without the leading '?'.</summary>
    public string QueryString
    {
        get
        {
            var index = Url.IndexOf('?', StringComparison.Ordinal);
            return index < 0 ? string.Empty : Url[(index + 1)..];
        }
    }
}
