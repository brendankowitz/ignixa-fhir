using System.Text.Json.Nodes;
using Ignixa.Specification.Generated;
using Ignixa.SqlOnFhir.Cli.Commands;
using Parquet;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class RunCommandIntegrationTests : IDisposable
{
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private static readonly string Views = Path.Combine(AppContext.BaseDirectory, "Fixtures", "views");
    private static readonly string Fhir  = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fhir");

    public RunCommandIntegrationTests() => Directory.CreateDirectory(_outDir);

    [Fact]
    public async Task GivenSingleViewAndInput_WhenRunCsv_ThenCsvCreatedWithHeaderAndRows()
    {
        var vdPath  = Path.Combine(Views, "patient-demographics.json");
        var inPath  = Path.Combine(Fhir,  "Patient.ndjson");
        var outPath = Path.Combine(_outDir, "out.csv");
        var cmd     = RunCommand.Create(new R4CoreSchemaProvider(), "r4");

        await cmd.Parse(["--views", vdPath, "--input", inPath, "--out", outPath, "--quiet"]).InvokeAsync();

        File.Exists(outPath).ShouldBeTrue();
        var lines = (await File.ReadAllLinesAsync(outPath)).Where(l => l.Length > 0).ToArray();
        lines.Length.ShouldBeGreaterThanOrEqualTo(2);
        lines[0].ShouldContain("id");
        lines[0].ShouldContain("family");
    }

    [Fact]
    public async Task GivenSingleViewAndInput_WhenRunNdjson_ThenNdjsonCreatedWithValidJsonLines()
    {
        var vdPath  = Path.Combine(Views, "patient-demographics.json");
        var inPath  = Path.Combine(Fhir,  "Patient.ndjson");
        var outPath = Path.Combine(_outDir, "out.ndjson");
        var cmd     = RunCommand.Create(new R4CoreSchemaProvider(), "r4");

        await cmd.Parse(["--views", vdPath, "--input", inPath, "--out", outPath, "--quiet"]).InvokeAsync();

        File.Exists(outPath).ShouldBeTrue();
        var lines = (await File.ReadAllLinesAsync(outPath)).Where(l => l.Length > 0).ToArray();
        lines.Length.ShouldBeGreaterThanOrEqualTo(1);
        foreach (var line in lines)
            Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(line));
    }

    [Fact]
    public async Task GivenSingleViewAndNestedOutput_WhenRunCsv_ThenCreatesParentDirectory()
    {
        var vdPath  = Path.Combine(Views, "patient-demographics.json");
        var inPath  = Path.Combine(Fhir,  "Patient.ndjson");
        var outPath = Path.Combine(_outDir, "nested", "out.csv");
        var cmd     = RunCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", vdPath, "--input", inPath, "--out", outPath, "--quiet"]).InvokeAsync();

        try
        {
            Environment.ExitCode.ShouldBe(0);
            File.Exists(outPath).ShouldBeTrue();
        }
        finally
        {
            Environment.ExitCode = 0;
        }
    }

    [Fact]
    public async Task GivenViewsAndInputDirs_WhenRunBatch_ThenOneOutputFilePerViewDef()
    {
        var cmd = RunCommand.Create(new R4CoreSchemaProvider(), "r4");

        await cmd.Parse(["--views", Views, "--input", Fhir, "--out", _outDir, "--format", "csv", "--quiet"]).InvokeAsync();

        File.Exists(Path.Combine(_outDir, "patient-demographics.csv")).ShouldBeTrue();
        File.Exists(Path.Combine(_outDir, "observation-codes.csv")).ShouldBeTrue();
    }

    [Fact]
    public async Task GivenBatchWithNoMatchingNdjson_WhenRun_ThenAllSkippedExitCode1()
    {
        var viewsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(viewsDir);
        await File.WriteAllTextAsync(Path.Combine(viewsDir, "allergy.json"),
            """{"resourceType":"ViewDefinition","resource":"AllergyIntolerance","select":[{"column":[{"name":"id","path":"id"}]}]}""");

        var cmd = RunCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", viewsDir, "--input", Fhir, "--out", _outDir, "--quiet"]).InvokeAsync();

        try { Environment.ExitCode.ShouldBe(1); }
        finally
        {
            Environment.ExitCode = 0;
            Directory.Delete(viewsDir, recursive: true);
        }
    }

    [Fact]
    public async Task GivenBatchRun_WhenStatsOutProvided_ThenStatsJsonWritten()
    {
        var statsPath = Path.Combine(_outDir, "stats.json");
        var cmd       = RunCommand.Create(new R4CoreSchemaProvider(), "r4");

        await cmd.Parse(["--views", Views, "--input", Fhir, "--out", _outDir,
                         "--format", "csv", "--quiet", "--stats-out", statsPath]).InvokeAsync();

        File.Exists(statsPath).ShouldBeTrue();
        var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(statsPath));
        json.RootElement.GetProperty("total").GetInt32().ShouldBeGreaterThan(0);
        json.RootElement.GetProperty("completed").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GivenComplexCollectionColumns_WhenRunParquet_ThenEachItemIsTheSourceJsonObject()
    {
        const string identifiers = """[{"system":"urn:example:a","value":"A-1"},{"system":"urn:example:b","value":"B-2"}]""";
        const string names = """[{"use":"official","family":"Doe","given":["Jane","Quinn"]}]""";

        var columns = await RunParquetAsync(
            """
            {"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[
              {"name":"id","path":"id","type":"id"},
              {"name":"identifiers","path":"identifier","type":"string","collection":true},
              {"name":"names","path":"name","collection":true},
              {"name":"given","path":"name.given","collection":true},
              {"name":"telecoms","path":"telecom","type":"string","collection":true}]}]}
            """,
            $$"""{"resourceType":"Patient","id":"p1","identifier":{{identifiers}},"name":{{names}}}""",
            """{"resourceType":"Patient","id":"p2"}""");

        columns["id"].ShouldBe(["p1", "p2"]);
        JsonNode.DeepEquals(JsonNode.Parse((string)columns["identifiers"][0]!), JsonNode.Parse(identifiers)).ShouldBeTrue((string?)columns["identifiers"][0]);
        JsonNode.DeepEquals(JsonNode.Parse((string)columns["names"][0]!), JsonNode.Parse(names)).ShouldBeTrue((string?)columns["names"][0]);
        JsonNode.DeepEquals(JsonNode.Parse((string)columns["given"][0]!), JsonNode.Parse("""["Jane","Quinn"]""")).ShouldBeTrue((string?)columns["given"][0]);
        columns["telecoms"].ShouldBe(["[]", "[]"]);
        columns["identifiers"][1].ShouldBe("[]");
        columns["names"][1].ShouldBe("[]");
    }

    [Fact]
    public async Task GivenTypedPrimitiveCollectionColumns_WhenRunParquet_ThenEachCellIsTheJsonArrayText()
    {
        var columns = await RunParquetAsync(
            """
            {"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[
              {"name":"id","path":"id","type":"id"},
              {"name":"birth","path":"birthDate","type":"date","collection":true},
              {"name":"activeFlag","path":"active","type":"boolean"},
              {"name":"active","path":"active","type":"boolean","collection":true}]}]}
            """,
            """{"resourceType":"Patient","id":"p1","birthDate":"1970-01-02","active":true}""",
            """{"resourceType":"Patient","id":"p2"}""");

        columns["birth"].ShouldBe(["""["1970-01-02"]""", "[]"]);
        columns["activeFlag"].ShouldBe([true, null]);
        columns["active"].ShouldBe(["[true]", "[]"]);
    }

    private async Task<Dictionary<string, List<object?>>> RunParquetAsync(string viewJson, params string[] resources)
    {
        var vdPath = Path.Combine(_outDir, "view.json");
        var inPath = Path.Combine(_outDir, "Patient.ndjson");
        var outPath = Path.Combine(_outDir, "out.parquet");
        await File.WriteAllTextAsync(vdPath, viewJson);
        await File.WriteAllLinesAsync(inPath, resources);
        var cmd = RunCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", vdPath, "--input", inPath, "--out", outPath, "--quiet"]).InvokeAsync();

        var exitCode = Environment.ExitCode;
        Environment.ExitCode = 0;
        exitCode.ShouldBe(0);
        return await ReadParquetColumnsAsync(outPath);
    }

    private static async Task<Dictionary<string, List<object?>>> ReadParquetColumnsAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        using var reader = await ParquetReader.CreateAsync(stream);
        var columns = reader.Schema.DataFields.ToDictionary(field => field.Name, _ => new List<object?>());
        for (var i = 0; i < reader.RowGroupCount; i++)
        {
            using var group = reader.OpenRowGroupReader(i);
            foreach (var field in reader.Schema.DataFields)
                columns[field.Name].AddRange((await group.ReadColumnAsync(field)).Data.Cast<object?>());
        }

        return columns;
    }

    public void Dispose()
    {
        Directory.Delete(_outDir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
