using Ignixa.SqlOnFhir.Cli.Batch;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests;

public class BatchProcessorTests : IAsyncDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public BatchProcessorTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public void GivenDirectory_WhenDiscoveringViews_ThenReturnsJsonFilesOnly()
    {
        File.WriteAllText(Path.Combine(_tempDir, "patient.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "obs.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "ignore");

        var files = BatchProcessor.DiscoverViewDefinitions(_tempDir, "**/*.json").ToList();

        files.Count.ShouldBe(2);
        files.ShouldAllBe(f => f.EndsWith(".json"));
    }

    [Fact]
    public void GivenSubdirectory_WhenDiscoveringViews_ThenSearchesRecursively()
    {
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.json"), "{}");

        var files = BatchProcessor.DiscoverViewDefinitions(_tempDir, "**/*.json").ToList();

        files.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenPathComponentPattern_WhenDiscoveringViews_ThenMatchesRelativePath()
    {
        var views = Path.Combine(_tempDir, "views");
        Directory.CreateDirectory(views);
        File.WriteAllText(Path.Combine(views, "patient.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "other.json"), "{}");

        var files = BatchProcessor.DiscoverViewDefinitions(_tempDir, "views/*.json")
            .Select(Path.GetFileName)
            .ToList();

        files.ShouldBe(["patient.json"]);
    }

    [Fact]
    public void GivenInputDirectory_WhenFindingFiles_ThenMatchesByResourceName()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Patient.ndjson"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "Observation.ndjson"), "{}");

        var files = BatchProcessor.FindInputFiles(_tempDir, "Patient", "*{resource}*.ndjson").ToList();

        files.Count.ShouldBe(1);
        Path.GetFileName(files[0]).ShouldBe("Patient.ndjson");
    }

    [Fact]
    public void GivenPathComponentPattern_WhenFindingInputFiles_ThenMatchesRelativePath()
    {
        var shard = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(shard);
        File.WriteAllText(Path.Combine(shard, "Patient.ndjson"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "Patient.ndjson"), "{}");

        var files = BatchProcessor.FindInputFiles(_tempDir, "Patient", "data/*{resource}*.ndjson")
            .Select(Path.GetFileName)
            .ToList();

        files.ShouldBe(["Patient.ndjson"]);
    }

    [Fact]
    public void GivenLowercaseFile_WhenFindingFiles_ThenMatchIsCaseInsensitive()
    {
        File.WriteAllText(Path.Combine(_tempDir, "patient.ndjson"), "{}");

        var files = BatchProcessor.FindInputFiles(_tempDir, "Patient", "*{resource}*.ndjson").ToList();

        files.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenShardedFiles_WhenFindingFiles_ThenReturnsAllInLexicographicOrder()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Patient_1.ndjson"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "Patient_2.ndjson"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "Patient_3.ndjson"), "{}");

        var files = BatchProcessor.FindInputFiles(_tempDir, "Patient", "*{resource}*.ndjson").ToList();

        files.Count.ShouldBe(3);
        files.ShouldBeInOrder();
    }

    [Fact]
    public void GivenNoMatchingFiles_WhenFindingFiles_ThenReturnsEmpty()
        => BatchProcessor.FindInputFiles(_tempDir, "AllergyIntolerance", "*{resource}*.ndjson").ShouldBeEmpty();

    [Fact]
    public void GivenViewDefinitionPath_WhenGettingOutputPath_ThenUsesBasenameAndFormat()
    {
        var outPath = BatchProcessor.GetOutputPath(
            Path.Combine(Path.GetTempPath(), "output"),
            Path.Combine(Path.GetTempPath(), "views", "patient-demographics.json"),
            "parquet");

        Path.GetFileName(outPath).ShouldBe("patient-demographics.parquet");
    }

    [Fact]
    public void GivenMultipleViewFiles_WhenDiscovering_ThenReturnedInLexicographicOrder()
    {
        File.WriteAllText(Path.Combine(_tempDir, "z-view.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "a-view.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "m-view.json"), "{}");

        var files = BatchProcessor.DiscoverViewDefinitions(_tempDir, "**/*.json")
            .Select(Path.GetFileName).ToList();

        files.ShouldBe(["a-view.json", "m-view.json", "z-view.json"]);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
