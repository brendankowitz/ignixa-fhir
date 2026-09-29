using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests;

public class StrictJsonPropertyGuardTests
{
    [Theory]
    [InlineData("manifest", false)]
    [InlineData("manifest", true)]
    [InlineData("resource", false)]
    [InlineData("resource", true)]
    [InlineData("metadata", false)]
    [InlineData("metadata", true)]
    public async Task GivenNestedDuplicateOrEscapeEquivalentProperties_WhenExtracted_ThenRejectsAtOwningPhysicalEntry(string kind, bool escaped)
    {
        string payload = escaped ? """{"items":[{"x":1,"\u0078":2}]}""" : """{"items":[{"x":1,"x":2}]}""";
        string manifest = kind == "manifest"
            ? $$"""{"name":"example.fhir.search","version":"1.2.0","extra":{{payload}}}"""
            : StrictPackageFixture.Manifest;
        string json = kind == "resource" ? $$"""{"resourceType":"Patient","extra":{{payload}}}""" : payload;
        byte[] bytes = StrictPackageFixture.Gzip(StrictPackageFixture.Tar(("package/package.json", manifest), ("package/data.json", json)));
        using var input = new MemoryStream(bytes);
        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        var error = await Should.ThrowAsync<PackageExtractionException>(() => extractor.ExtractStrictAsync(input, new(), CancellationToken.None));
        error.Diagnostic.Code.ShouldBe(kind == "manifest" ? PackageExtractionError.InvalidManifest : PackageExtractionError.InvalidJson);
        error.Diagnostic.EntryIndex.ShouldBe(kind == "manifest" ? 1 : 2);
    }

    [Fact]
    public async Task GivenSamePropertyInSeparateArrayObjects_WhenExtracted_ThenRetainsRawJsonWithoutFalseDuplicate()
    {
        const string json = """{"items":[{"x":1},{"\u0078":2},{"x":3}]}""";
        using var input = new MemoryStream(StrictPackageFixture.Package(("package/data.json", json)));
        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        (await extractor.ExtractStrictAsync(input, new(), CancellationToken.None)).JsonEntries.Single().Json.ShouldBe(json);
    }
}
