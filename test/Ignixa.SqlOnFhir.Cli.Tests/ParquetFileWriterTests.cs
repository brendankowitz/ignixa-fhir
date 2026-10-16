using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests;

public class ParquetFileWriterTests : IAsyncDisposable
{
    private readonly string _outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.parquet");

    [Fact]
    public async Task GivenAStream_WhenRowsAreWritten_ThenTheFileIsFinishedOnThatStreamAndItIsClosed()
    {
        var stream = new FileStream(_outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var schema = new ParquetSchema(new DataField<string>("id"));
        await using (var writer = new ParquetFileWriter(stream, schema, NullLogger.Instance, new Dictionary<string, string> { ["id"] = "STRING" }))
        {
            await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p1" });
            await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p2" });
            await writer.FlushAsync();
            writer.BytesWritten.ShouldBeGreaterThan(0);
        }

        stream.CanWrite.ShouldBeFalse();
        await using FileStream read = File.OpenRead(_outputPath);
        using ParquetReader reader = await ParquetReader.CreateAsync(read);
        using ParquetRowGroupReader group = reader.OpenRowGroupReader(0);
        DataColumn ids = await group.ReadColumnAsync(reader.Schema.DataFields[0]);
        ids.Data.Cast<string>().ShouldBe(["p1", "p2"]);
    }

    [Fact]
    public void GivenAStreamThatCannotSeek_WhenTheWriterIsCreated_ThenItIsRefusedUpFront()
    {
        using var inner = new MemoryStream();
        var schema = new ParquetSchema(new DataField<string>("id"));

        Should.Throw<ArgumentException>(() => new ParquetFileWriter(new WriteOnlyStream(inner), schema, NullLogger.Instance)).ParamName.ShouldBe("stream");
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(_outputPath)) File.Delete(_outputPath);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
