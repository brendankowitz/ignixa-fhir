using System.Formats.Tar;
using System.Text;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.PackageManagement.Tests;

public class StrictParserDifferentialTests(ITestOutputHelper output)
{
    private readonly PackageExtractor _extractor = new(NullLogger<PackageExtractor>.Instance);

    [Fact]
    public async Task GivenLeadingNulChecksum_WhenStrictExtracting_ThenCannotSilentlyStopAfterManifest()
    {
        (byte[] gzip, byte[] tar) = ReadWire("nul-checksum");
        tar[1024 + 148].ShouldBe((byte)0);

        ReadBclNames(tar).ShouldBe(Environment.Version.Major == 9
            ? ["package/package.json", "package/resource.json"]
            : ["package/package.json"]);
        await AssertRejected(gzip, PackageExtractionError.InvalidArchive, 2);
    }

    [Fact]
    public async Task GivenPaxNewlineSizeInjection_WhenStrictExtracting_ThenCannotSwallowFollowingResource()
    {
        const string payload = "26 comment=x\n13 size=1024\n";
        (byte[] gzip, byte[] tar) = ReadWire("pax-newline-size");
        Encoding.UTF8.GetString(tar, 1536, 26).ShouldBe(payload);
        tar[1024 + 156].ShouldBe((byte)'x');

        if (Environment.Version.Major == 9)
        {
            ReadBclNames(tar).ShouldBe(["package/package.json", "package/ignored.bin"]);
        }
        else
        {
            Should.Throw<InvalidDataException>(() => ReadBclNames(tar));
            output.WriteLine($"Runtime {Environment.Version}; BCL rejected embedded-newline Pax records.");
        }
        await AssertRejected(gzip, PackageExtractionError.InvalidArchive, 2);
    }

    [Fact]
    public async Task GivenTrailingSpaceWirePath_WhenStrictExtracting_ThenRejectsBeforeLossyNameNormalization()
    {
        (byte[] gzip, byte[] tar) = ReadWire("trailing-space-path");
        Encoding.UTF8.GetString(tar, 1024, 19).ShouldBe("package/evil.json \0");

        ReadBclNames(tar).ShouldBe(["package/package.json",
            Environment.Version.Major == 9 ? "package/evil.json" : "package/evil.json "]);
        await AssertRejected(gzip, PackageExtractionError.UnsafePath, 2);
    }

    private (byte[] Gzip, byte[] Tar) ReadWire(string name)
    {
        (byte[] gzip, byte[] tar) = StrictWireFixture.Load(name);
        output.WriteLine($"WIRE {name}; gzip={gzip.Length}:{StrictWireFixture.Hash(gzip)}; tar={tar.Length}:{StrictWireFixture.Hash(tar)}");
        return (gzip, tar);
    }

    private string[] ReadBclNames(byte[] tar)
    {
        using var stream = new MemoryStream(tar);
        using var reader = new TarReader(stream);
        var names = new List<string>();
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            names.Add(entry.Name);
        }
        output.WriteLine($"Runtime {Environment.Version}; BCL returned {names.Count}: {string.Join(", ", names)}");
        return names.ToArray();
    }

    private async Task AssertRejected(byte[] gzip, PackageExtractionError code, int physicalIndex)
    {
        using var input = new MemoryStream(gzip);
        Exception? failure = await Record.ExceptionAsync(async () =>
        {
            var result = await _extractor.ExtractStrictAsync(input, new(), CancellationToken.None);
            output.WriteLine($"Strict returned SUCCESS: physical={result.Statistics.PhysicalEntries}, " +
                $"logical={result.Statistics.LogicalEntries}, JSON entries={result.JsonEntries.Count}");
        });
        output.WriteLine($"Strict failure: {failure?.GetType().Name ?? "none"}; " +
            $"diagnostic={(failure as PackageExtractionException)?.Diagnostic}");
        var exception = failure.ShouldBeOfType<PackageExtractionException>();
        exception.Diagnostic.Code.ShouldBe(code);
        exception.Diagnostic.EntryIndex.ShouldBe(physicalIndex);
        input.CanRead.ShouldBeTrue();
    }
}
