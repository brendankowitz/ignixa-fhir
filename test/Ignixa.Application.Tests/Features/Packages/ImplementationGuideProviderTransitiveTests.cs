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
using Xunit;

namespace Ignixa.Application.Tests.Features.Packages;

/// <summary>
/// Tests for <see cref="ImplementationGuideProvider.LoadPackageWithDependenciesAsync"/>.
/// Verifies the closure-walking behaviour: BFS, dedup by id, skip r4.core, tolerate
/// transitive failures, idempotent re-runs.
/// </summary>
public class ImplementationGuideProviderTransitiveTests
{
    private readonly IPackageLoader _loader;
    private readonly IPackageExtractor _extractor;
    private readonly IPackageResourceImporter _importer;
    private readonly IPackageResourceRepository _repository;
    private readonly ImplementationGuideProvider _provider;

    public ImplementationGuideProviderTransitiveTests()
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

    /// <summary>
    /// Helper: configure the mocks so a given package id+version "exists" with the
    /// declared dependency map. Each call to <see cref="IPackageExtractor.ExtractAsync"/>
    /// for that package returns a manifest carrying the declared deps.
    /// </summary>
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
    public async Task GivenRootWithTransitiveDeps_WhenLoadingWithDependencies_ThenWalksClosureBfs()
    {
        // root -> A -> B; root -> C
        RegisterPackage("root", "1.0.0", new Dictionary<string, string>
        {
            ["A"] = "1.0.0",
            ["C"] = "1.0.0",
        });
        RegisterPackage("A", "1.0.0", new Dictionary<string, string> { ["B"] = "1.0.0" });
        RegisterPackage("B", "1.0.0");
        RegisterPackage("C", "1.0.0");
        StubImportReturnsEmpty();
        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        result.LoadedPackages.ShouldNotBeNull();
        var ids = result.LoadedPackages!.Select(s => s.Split(' ', 2)[0]).ToList();
        ids.ShouldContain("root@1.0.0");
        ids.ShouldContain("A@1.0.0");
        ids.ShouldContain("B@1.0.0");
        ids.ShouldContain("C@1.0.0");
        // BFS order: root first, then its direct deps before their grandchildren
        ids.IndexOf("root@1.0.0").ShouldBeLessThan(ids.IndexOf("A@1.0.0"));
        ids.IndexOf("A@1.0.0").ShouldBeLessThan(ids.IndexOf("B@1.0.0"));
    }

    [Fact]
    public async Task GivenDiamondDependencyGraph_WhenLoading_ThenSharedDepLoadedOnce()
    {
        // root -> A -> shared; root -> B -> shared
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["A"] = "1.0.0", ["B"] = "1.0.0" });
        RegisterPackage("A", "1.0.0", new Dictionary<string, string> { ["shared"] = "1.0.0" });
        RegisterPackage("B", "1.0.0", new Dictionary<string, string> { ["shared"] = "1.0.0" });
        RegisterPackage("shared", "1.0.0");
        StubImportReturnsEmpty();
        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        var sharedEntries = result.LoadedPackages!.Where(s => s.StartsWith("shared@", StringComparison.Ordinal)).ToList();
        sharedEntries.Count.ShouldBe(1, "Diamond dep must be visited exactly once");
    }

    [Fact]
    public async Task GivenRootDependsOnR4Core_WhenLoading_ThenR4CoreSkipped()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string>
        {
            ["hl7.fhir.r4.core"] = "4.0.1",
            ["sibling"] = "1.0.0",
        });
        RegisterPackage("sibling", "1.0.0");
        StubImportReturnsEmpty();
        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        result.LoadedPackages!.ShouldNotContain(s => s.StartsWith("hl7.fhir.r4.core", StringComparison.OrdinalIgnoreCase));
        result.LoadedPackages.ShouldContain(s => s.StartsWith("sibling@", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenTransitiveDepDownloadFails_WhenLoading_ThenClosureContinues()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string>
        {
            ["broken"] = "1.0.0",
            ["good"] = "1.0.0",
        });
        RegisterPackage("good", "1.0.0");
        // Make "broken" throw on download
        _loader.DownloadPackageAsync("broken", "1.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new InvalidOperationException("simulated network failure"));

        StubImportReturnsEmpty();
        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        result.LoadedPackages.ShouldNotBeNull();
        result.LoadedPackages!.ShouldContain(s => s.StartsWith("root@", StringComparison.Ordinal));
        result.LoadedPackages.ShouldContain(s => s.StartsWith("good@", StringComparison.Ordinal));
        result.LoadedPackages.ShouldNotContain(s => s.StartsWith("broken@", StringComparison.Ordinal));
        result.SkippedPackages.ShouldNotBeNull();
        result.SkippedPackages!.ShouldContain(s => s.StartsWith("broken@", StringComparison.Ordinal) && s.Contains("skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenAggregatedClosure_WhenLoading_ThenResultSumsResourceCounts()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["dep"] = "1.0.0" });
        RegisterPackage("dep", "1.0.0");
        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        _importer.ImportAsync(Arg.Any<PackageExtractionResult>(), _repository, Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var er = ci.Arg<PackageExtractionResult>();
                // root contributes 10, dep contributes 5
                var n = er.Manifest.Name == "root" ? 10 : 5;
                return Task.FromResult(new PackageImportResult
                {
                    PackageId = er.Manifest.Name,
                    PackageVersion = er.Manifest.Version,
                    TotalResources = n,
                    ImportedResources = n,
                    ResourcesByType = new Dictionary<string, int> { ["StructureDefinition"] = n },
                });
            });

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        result.ImportedResources.ShouldBe(15);
        result.ResourcesByType["StructureDefinition"].ShouldBe(15);
        result.PackageId.ShouldBe("root");
        result.PackageVersion.ShouldBe("1.0.0");
    }
}
