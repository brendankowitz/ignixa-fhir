using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests;

public class CsvFileWriterTests : IAsyncDisposable
{
    private readonly string _outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");

    [Fact]
    public async Task GivenRows_WhenWritten_ThenTheFileStartsWithTheHeaderAndNoByteOrderMark()
    {
        await using (var writer = new CsvFileWriter(_outputPath, NullLogger.Instance))
        {
            await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p1", ["name"] = "Smith" });
            await writer.FlushAsync();
        }

        byte[] bytes = await File.ReadAllBytesAsync(_outputPath);
        bytes[0].ShouldBe((byte)'i');
        System.Text.Encoding.UTF8.GetString(bytes).ShouldStartWith("id,name");
    }

    [Fact]
    public async Task GivenAStream_WhenRowsAreWritten_ThenTheyGoToThatStreamAndItIsClosed()
    {
        var stream = new FileStream(_outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        await using (var writer = new CsvFileWriter(stream, NullLogger.Instance))
        {
            await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p1" });
            await writer.FlushAsync();
            writer.BytesWritten.ShouldBe(stream.Length);
        }

        (await File.ReadAllTextAsync(_outputPath)).ShouldBe("id" + Environment.NewLine + "p1" + Environment.NewLine);
        stream.CanWrite.ShouldBeFalse();
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(_outputPath)) File.Delete(_outputPath);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
