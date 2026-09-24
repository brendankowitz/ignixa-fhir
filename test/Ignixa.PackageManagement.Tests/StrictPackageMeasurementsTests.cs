using System.Text;
using System.Text.Json;
using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.PackageManagement.Tests;

public class StrictPackageMeasurementsTests(ITestOutputHelper output)
{
    private static readonly string[] PatientBase = ["Patient"];
    private static readonly string[] GivenName = ["Example"];

    [Theory]
    [InlineData(6, 2, 11)]
    [InlineData(1000, 250, 1253)]
    public async Task GivenRepresentativeSyntheticPackage_WhenStrictExtracting_ThenDefaultsFitAndAccountingMatchesWire(
        int searchParameters, int patients, int physicalEntries)
    {
        var files = new List<(string Path, string Json)>
        {
            ("package/package.json", StrictPackageFixture.Manifest),
            ("package/.index.json", """{"index-version":1,"files":[]}"""),
            ("package/README.md", "Synthetic fixture. No scripts are executed.")
        };
        for (int i = 0; i < searchParameters; i++)
        {
            files.Add(($"package/SearchParameter-sp-{i}.json", JsonSerializer.Serialize(new
            {
                resourceType = "SearchParameter",
                id = $"sp-{i}",
                url = $"https://example.org/SearchParameter/sp-{i}",
                version = "1.2.0",
                name = $"SearchParameter{i}",
                status = "active",
                description = $"Synthetic search parameter {i} for bounded package extraction measurements.",
                code = $"sp-{i}",
                @base = PatientBase,
                type = "string",
                expression = "Patient.name",
                component = new[] { new { definition = "https://example.org/SearchParameter/name", expression = "family" } }
            })));
        }
        for (int i = 0; i < patients; i++)
        {
            files.Add(($"package/Patient-p-{i}.json", JsonSerializer.Serialize(new
            {
                resourceType = "Patient",
                id = $"p-{i}",
                name = new[] { new { family = $"Synthetic{i}", given = GivenName } }
            })));
        }
        byte[] tar = StrictPackageFixture.Tar(files.ToArray());
        byte[] gzip = StrictPackageFixture.Gzip(tar);
        using var input = new StrictInputStream(gzip);
        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);

        var result = await extractor.ExtractStrictAsync(input, new(), CancellationToken.None);

        result.JsonEntries.Count(e => e.ResourceType == "SearchParameter").ShouldBe(searchParameters);
        result.JsonEntries.Count(e => e.ResourceType == "Patient").ShouldBe(patients);
        result.JsonEntries.Count(e => e.ResourceType is null).ShouldBe(1);
        result.Statistics.PhysicalEntries.ShouldBe(physicalEntries);
        result.Statistics.LogicalEntries.ShouldBe(physicalEntries);
        result.Statistics.CompressedBytes.ShouldBe(gzip.Length);
        result.Statistics.ExpandedBytes.ShouldBe(tar.Length);
        result.Statistics.PayloadBytes.ShouldBe(files.Sum(f => (long)Encoding.UTF8.GetByteCount(f.Json)));
        int depth = files.Where(f => f.Path.EndsWith(".json", StringComparison.Ordinal))
            .Max(f => MaximumDepth(Encoding.UTF8.GetBytes(f.Json)));
        depth.ShouldBe(4);
        output.WriteLine(
            $"SearchParameters={searchParameters}; Patients={patients}; CompressedBytes={gzip.Length}; ExpandedBytes={tar.Length}; " +
            $"LargestEntry={files.Max(f => Encoding.UTF8.GetByteCount(f.Json))}; PayloadBytes={result.Statistics.PayloadBytes}; " +
            $"PhysicalEntries={physicalEntries}; LogicalEntries={result.Statistics.LogicalEntries}; JsonDepth={depth}");
    }

    private static int MaximumDepth(byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        int depth = 0;
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
            {
                depth = Math.Max(depth, reader.CurrentDepth + 1);
            }
        }
        return depth;
    }
}
