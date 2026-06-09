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
/// Integration test for <see cref="ImplementationGuideProvider.LoadPackageWithDependenciesAsync"/>
/// that exercises the real <see cref="NpmPackageLoader"/> + <see cref="PackageExtractor"/> chain
/// against an actual package on <c>packages.fhir.org</c>. The repository and importer are
/// substituted so the test doesn't depend on a database.
/// <para>
/// First run downloads ~10 MB into the local NPM cache; subsequent runs hit the cache.
/// </para>
/// </summary>
public class ImplementationGuideProviderTransitiveIntegrationTests
{
    private static ImplementationGuideProvider BuildProviderWithRealNetwork(
        out IPackageResourceImporter importer,
        out IPackageResourceRepository repository)
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "ignixa-test-package-cache");
        Directory.CreateDirectory(cacheDir);
        var cacheManager = new PackageCacheManager(cacheDir, NullLogger<PackageCacheManager>.Instance);

        // Note: HttpClient is intentionally not disposed here - this is a test helper used
        // by a single test method and the process exits shortly after.
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var loader = new NpmPackageLoader(httpClient, cacheManager, options: null, NullLogger<NpmPackageLoader>.Instance);
        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);

        importer = Substitute.For<IPackageResourceImporter>();
        repository = Substitute.For<IPackageResourceRepository>();

        // Default: nothing already loaded, importer returns a result mirroring the extraction.
        repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);
        importer.ImportAsync(Arg.Any<PackageExtractionResult>(), repository, Arg.Any<CancellationToken>())
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

        return new ImplementationGuideProvider(
            loader, extractor, importer, repository,
            NullLogger<ImplementationGuideProvider>.Instance);
    }

    [Fact]
    [Trait("Category", "RequiresNetwork")]
    public async Task GivenAuCoreRoot_WhenLoadingWithDependenciesViaProvider_ThenAllDeclaredDepsImported()
    {
        var provider = BuildProviderWithRealNetwork(out var importer, out _);

        var result = await provider.LoadPackageWithDependenciesAsync(
            tenantId: "1",
            packageId: "hl7.fhir.au.core",
            version: "1.0.0",
            cancellationToken: CancellationToken.None);

        result.LoadedPackages.ShouldNotBeNull();
        var ids = result.LoadedPackages!.Select(s => s.Split(' ', 2)[0]).ToList();

        // AU Core 1.0.0 declares these in its package.json dependencies block.
        ids.ShouldContain("hl7.fhir.au.core@1.0.0");
        ids.ShouldContain(s => s.StartsWith("hl7.fhir.au.base@", StringComparison.Ordinal));
        ids.ShouldContain(s => s.StartsWith("hl7.terminology.r4@", StringComparison.Ordinal));
        ids.ShouldContain(s => s.StartsWith("hl7.fhir.uv.extensions.r4@", StringComparison.Ordinal));

        // r4 core must always be skipped (in-process schema provider, not on disk).
        ids.ShouldNotContain(s => s.StartsWith("hl7.fhir.r4.core@", StringComparison.Ordinal));

        // Importer should have been called at least once per visited package that wasn't
        // already loaded (we mocked repository to say nothing is loaded).
        await importer.Received().ImportAsync(
            Arg.Is<PackageExtractionResult>(e => e.Manifest.Name == "hl7.fhir.au.core"),
            Arg.Any<IPackageResourceRepository>(),
            Arg.Any<CancellationToken>());
        await importer.Received().ImportAsync(
            Arg.Is<PackageExtractionResult>(e => e.Manifest.Name == "hl7.fhir.au.base"),
            Arg.Any<IPackageResourceRepository>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "RequiresNetwork")]
    public async Task GivenCarinBbRoot_WhenLoadingWithDependenciesViaProvider_ThenClosureIsSelfContained()
    {
        var provider = BuildProviderWithRealNetwork(out _, out _);

        var result = await provider.LoadPackageWithDependenciesAsync(
            tenantId: "1",
            packageId: "hl7.fhir.us.carin-bb",
            version: "2.1.0",
            cancellationToken: CancellationToken.None);

        result.LoadedPackages.ShouldNotBeNull();
        var ids = result.LoadedPackages!.Select(s => s.Split(' ', 2)[0]).ToList();

        ids.ShouldContain("hl7.fhir.us.carin-bb@2.1.0");
        // CARIN-BB does not depend on AU Base - sanity check that we don't pick up
        // packages from unrelated previously-tested IGs.
        ids.ShouldNotContain(s => s.StartsWith("hl7.fhir.au.base@", StringComparison.Ordinal));
        ids.ShouldNotContain(s => s.StartsWith("hl7.fhir.au.core@", StringComparison.Ordinal));
    }
}
