using Ignixa.TestScript.Model;
using Ignixa.TestScript.Parsing;

namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>
/// Parse-once cache of every TestScript under a folder, loaded at <c>serve</c> startup so <c>/run</c>
/// evaluates an already-parsed <see cref="TestScriptDefinition"/> per call instead of re-parsing the
/// file on every request.
/// </summary>
internal sealed class TestScriptRegistry
{
    private readonly IReadOnlyList<TestScriptRegistryEntry> _entries;
    private readonly IReadOnlyDictionary<string, TestScriptRegistryEntry> _byId;

    private TestScriptRegistry(IReadOnlyList<TestScriptRegistryEntry> entries)
    {
        _entries = entries;
        _byId = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<TestScriptRegistryEntry> Entries => _entries;

    public int Count => _entries.Count;

    public int InvalidCount => _entries.Count(e => e.ParseError is not null);

    public TestScriptRegistryEntry? TryGet(string id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// Parses every <c>*.json</c> file under <paramref name="testsPath"/> once. A file whose stem
    /// (file name without extension) is unique among all discovered files is addressed by that stem
    /// (e.g. "PatientSearch" for "Search/PatientSearch.json"); when two or more files share a stem,
    /// every one of those entries instead uses its relative path without extension (e.g.
    /// "Search/Basic", "CRUD/Basic") so every script stays addressable by a distinct, predictable id.
    /// </summary>
    public static TestScriptRegistry Load(string testsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testsPath);

        var files = Directory.EnumerateFiles(testsPath, "*.json", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var stemCounts = files
            .GroupBy(f => Path.GetFileNameWithoutExtension(f)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<TestScriptRegistryEntry>();
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(testsPath, file).Replace('\\', '/');
            var stem = Path.GetFileNameWithoutExtension(file)!;
            var id = stemCounts[stem] > 1
                ? relativePath[..^Path.GetExtension(relativePath).Length]
                : stem;

            entries.Add(ParseEntry(id, relativePath, file));
        }

        return new TestScriptRegistry(entries);
    }

    private static TestScriptRegistryEntry ParseEntry(string id, string relativePath, string file)
    {
        ParseResult<TestScriptDefinition> parseResult;
        try
        {
            parseResult = TestScriptParser.ParseFile(file);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TestScriptRegistryEntry(id, id, relativePath, null, $"{ex.GetType().Name}: {ex.Message}");
        }

        if (!parseResult.IsSuccess || parseResult.Value is null)
        {
            var message = parseResult.Errors.Count > 0
                ? string.Join("; ", parseResult.Errors.Select(e => e.Message))
                : "Unknown parse failure";
            return new TestScriptRegistryEntry(id, id, relativePath, null, message);
        }

        return new TestScriptRegistryEntry(id, parseResult.Value.Metadata.Name, relativePath, parseResult.Value, null);
    }
}
