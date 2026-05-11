using System.Text.Json;
using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests;

public class NdjsonFileWriterTests : IAsyncDisposable
{
    private readonly string _outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ndjson");

    [Fact]
    public async Task GivenSingleRow_WhenWritten_ThenFileContainsValidJsonLine()
    {
        await using var writer = new NdjsonFileWriter(_outputPath, NullLogger.Instance);
        var row = new Dictionary<string, object?> { ["id"] = "p1", ["name"] = "Smith" };

        await writer.WriteRowAsync(row);
        await writer.FlushAsync();

        var lines = await File.ReadAllLinesAsync(_outputPath);
        lines.Length.ShouldBe(1);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(lines[0])!;
        parsed["id"].GetString().ShouldBe("p1");
        parsed["name"].GetString().ShouldBe("Smith");
    }

    [Fact]
    public async Task GivenMultipleRows_WhenWritten_ThenEachRowIsASeparateLine()
    {
        await using var writer = new NdjsonFileWriter(_outputPath, NullLogger.Instance);

        await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p1" });
        await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p2" });
        await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p3" });
        await writer.FlushAsync();

        var lines = (await File.ReadAllLinesAsync(_outputPath))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        lines.Length.ShouldBe(3);
        writer.RowsWritten.ShouldBe(3);
    }

    [Fact]
    public async Task GivenNullValue_WhenWritten_ThenFieldIsJsonNull()
    {
        await using var writer = new NdjsonFileWriter(_outputPath, NullLogger.Instance);

        await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p1", ["name"] = null });
        await writer.FlushAsync();

        var lines = await File.ReadAllLinesAsync(_outputPath);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(lines[0])!;
        parsed["name"].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GivenRows_WhenFlushed_ThenBytesWrittenIsPositive()
    {
        await using var writer = new NdjsonFileWriter(_outputPath, NullLogger.Instance);

        await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p1" });
        await writer.FlushAsync();

        writer.BytesWritten.ShouldBeGreaterThan(0);
    }

    public async ValueTask DisposeAsync()
    {
        if (File.Exists(_outputPath)) File.Delete(_outputPath);
        GC.SuppressFinalize(this);
    }
}
