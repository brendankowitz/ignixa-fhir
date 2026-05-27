// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.PackageManagement.Tests;

/// <summary>
/// Tests for the <c>Dependencies</c> field on <see cref="Ignixa.PackageManagement.Models.PackageManifest"/>,
/// added so transitive IG package resolution can walk a package's declared dependencies.
/// </summary>
public class PackageManifestDependenciesTests
{
    private static async Task<Stream> BuildTarballAsync(string packageJson)
    {
        // Construct an in-memory .tgz containing only package/package.json.
        // PackageExtractor scans tar entries ending in package.json.
        var ms = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new System.Formats.Tar.TarWriter(gzip, System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(packageJson);
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "package/package.json")
            {
                DataStream = new MemoryStream(bytes),
            };
            await tar.WriteEntryAsync(entry);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task GivenPackageJsonWithDependencies_WhenExtracting_ThenManifestExposesDependencies()
    {
        const string packageJson = """
            {
              "name": "hl7.fhir.au.core",
              "version": "1.0.0",
              "fhirVersion": "4.0.1",
              "dependencies": {
                "hl7.fhir.r4.core": "4.0.1",
                "hl7.fhir.au.base": "5.0.0",
                "hl7.terminology.r4": "6.2.0"
              }
            }
            """;
        await using var tarball = await BuildTarballAsync(packageJson);

        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        var result = await extractor.ExtractAsync(tarball, CancellationToken.None);

        result.Manifest.Dependencies.ShouldNotBeNull();
        result.Manifest.Dependencies!["hl7.fhir.r4.core"].ShouldBe("4.0.1");
        result.Manifest.Dependencies["hl7.fhir.au.base"].ShouldBe("5.0.0");
        result.Manifest.Dependencies["hl7.terminology.r4"].ShouldBe("6.2.0");
    }

    [Fact]
    public async Task GivenPackageJsonWithoutDependencies_WhenExtracting_ThenManifestDependenciesIsNullOrEmpty()
    {
        const string packageJson = """
            { "name": "hl7.fhir.example", "version": "1.0.0", "fhirVersion": "4.0.1" }
            """;
        await using var tarball = await BuildTarballAsync(packageJson);

        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        var result = await extractor.ExtractAsync(tarball, CancellationToken.None);

        // Either null or empty is acceptable; both convey "no transitive dependencies".
        (result.Manifest.Dependencies == null || result.Manifest.Dependencies.Count == 0).ShouldBeTrue();
    }
}
