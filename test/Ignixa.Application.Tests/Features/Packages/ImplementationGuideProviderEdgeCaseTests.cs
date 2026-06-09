// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Domain.Abstractions;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Packages;

/// <summary>
/// Additional edge-case tests for <see cref="ImplementationGuideProvider.LoadPackageWithDependenciesAsync"/>
/// covering root failure propagation, core-package guarding, manifest re-fetch failure visibility,
/// and version conflict recording.
/// </summary>
public class ImplementationGuideProviderEdgeCaseTests
{
    private readonly IPackageLoader _loader;
    private readonly IPackageExtractor _extractor;
    private readonly IPackageResourceImporter _importer;
    private readonly IPackageResourceRepository _repository;
    private readonly ImplementationGuideProvider _provider;

    public ImplementationGuideProviderEdgeCaseTests()
    {
        _loader = Substitute.For<IPackageLoader>();
        _extractor = Substitute.For<IPackageExtractor>();
        _importer = Substitute.For<IPackageResourceImporter>();
        _repository = Substitute.For<IPackageResourceRepository>();
        _provider = new ImplementationGuideProvider(
            _loader,
            _extractor,
            _importer,
            _repository,
            NullLogger<ImplementationGuideProvider>.Instance);
    }

    private void RegisterPackage(string id, string version, IReadOnlyDictionary<string, string>? deps = null)
    {
        _loader.DownloadPackageAsync(id, version, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("mock-" + id))));

        _extractor.ExtractAsync(
            Arg.Is<Stream>(s => StreamMatchesPackage(s, id)),
            Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new PackageExtractionResult
            {
                Manifest = new PackageManifest
                {
                    Name = id,
                    Version = version,
                    FhirVersion = "4.0.1",
                    Dependencies = deps ?? System.Collections.Frozen.FrozenDictionary<string, string>.Empty,
                },
                Resources = Array.Empty<ExtractedResource>(),
            }));
    }

    private static bool StreamMatchesPackage(Stream s, string id)
    {
        if (s is not MemoryStream ms) return false;
        var bytes = ms.ToArray();
        ms.Position = 0;
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        return content == "mock-" + id;
    }

    private void StubImportReturnsEmpty()
    {
        _importer.ImportAsync(Arg.Any<PackageExtractionResult>(), _repository, Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var er = ci.Arg<PackageExtractionResult>();
                return Task.FromResult(new PackageImportResult
                {
                    PackageId = er.Manifest.Name,
                    PackageVersion = er.Manifest.Version,
                    TotalResources = er.Resources.Count,
                    ImportedResources = er.Resources.Count,
                    ResourcesByType = new Dictionary<string, int>(),
                });
            });
    }

    [Fact]
    public async Task GivenRootDownloadFails_WhenLoadingWithDependencies_ThenExceptionPropagates()
    {
        // Arrange
        _loader.DownloadPackageAsync("root", "1.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new InvalidOperationException("registry unavailable"));

        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act / Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None));
    }

    [Fact]
    public async Task GivenTransitiveDepFails_WhenLoadingWithDependencies_ThenContinuesAndRecordsSkip()
    {
        // Arrange
        RegisterPackage("root", "1.0.0", new Dictionary<string, string>
        {
            ["broken"] = "2.0.0",
            ["good"] = "1.0.0",
        });
        RegisterPackage("good", "1.0.0");
        _loader.DownloadPackageAsync("broken", "2.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new HttpRequestException("network error"));

        StubImportReturnsEmpty();
        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        // Assert
        result.LoadedPackages.ShouldNotBeNull();
        result.LoadedPackages!.ShouldContain(s => s.StartsWith("root@", StringComparison.Ordinal));
        result.LoadedPackages.ShouldContain(s => s.StartsWith("good@", StringComparison.Ordinal));
        result.LoadedPackages.ShouldNotContain(s => s.StartsWith("broken@", StringComparison.Ordinal));
        result.SkippedPackages.ShouldNotBeNull();
        result.SkippedPackages!.ShouldContain(s => s.StartsWith("broken@2.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenTransitiveDepIsCorePackage_WhenLoadingWithDependencies_ThenNotLoadedAndRecordedInSkipped()
    {
        // Arrange — root declares hl7.fhir.r5.core as a dep (core package via back-door)
        RegisterPackage("root", "1.0.0", new Dictionary<string, string>
        {
            ["hl7.fhir.r5.core"] = "5.0.0",
            ["sibling"] = "1.0.0",
        });
        RegisterPackage("sibling", "1.0.0");
        StubImportReturnsEmpty();
        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        // Assert
        result.LoadedPackages.ShouldNotBeNull();
        result.LoadedPackages!.ShouldNotContain(s => s.StartsWith("hl7.fhir.r5.core", StringComparison.OrdinalIgnoreCase));
        result.SkippedPackages.ShouldNotBeNull();
        result.SkippedPackages!.ShouldContain(s =>
            s.StartsWith("hl7.fhir.r5.core", StringComparison.OrdinalIgnoreCase) &&
            s.Contains("core package", StringComparison.Ordinal));
        await _loader.DidNotReceive().DownloadPackageAsync(
            "hl7.fhir.r5.core", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAlreadyLoadedPackageManifestRefetchFails_WhenLoading_ThenSkippedEntryRecorded()
    {
        // Arrange — first call returns "already loaded" (package exists), second download throws
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["dep"] = "1.0.0" });

        _repository.PackageVersionExistsAsync("dep", "1.0.0", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _repository.PackageVersionExistsAsync("root", "1.0.0", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // dep already loaded → short-circuit returns (null manifest); re-fetch will fail
        _loader.DownloadPackageAsync("dep", "1.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new InvalidOperationException("tarball gone"));

        StubImportReturnsEmpty();

        // Act
        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        // Assert
        result.SkippedPackages.ShouldNotBeNull();
        result.SkippedPackages!.ShouldContain(s =>
            s.StartsWith("dep@1.0.0", StringComparison.Ordinal) &&
            s.Contains("manifest unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenVersionConflictInGraph_WhenLoading_ThenConflictingVersionSkippedAndRecorded()
    {
        // Arrange — root -> A@1.0.0 and root -> B -> A@2.0.0 (conflict on A)
        RegisterPackage("root", "1.0.0", new Dictionary<string, string>
        {
            ["A"] = "1.0.0",
            ["B"] = "1.0.0",
        });
        RegisterPackage("A", "1.0.0");
        RegisterPackage("B", "1.0.0", new Dictionary<string, string> { ["A"] = "2.0.0" });
        RegisterPackage("A", "2.0.0");
        StubImportReturnsEmpty();
        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        // Assert
        result.SkippedPackages.ShouldNotBeNull();
        result.SkippedPackages!.ShouldContain(s =>
            s.StartsWith("A@2.0.0", StringComparison.Ordinal) &&
            s.Contains("version conflict", StringComparison.Ordinal));
        result.LoadedPackages.ShouldNotBeNull();
        result.LoadedPackages!.ShouldContain(s => s.StartsWith("A@1.0.0", StringComparison.Ordinal));
        result.LoadedPackages.ShouldNotContain(s => s.StartsWith("A@2.0.0", StringComparison.Ordinal));
    }
}
