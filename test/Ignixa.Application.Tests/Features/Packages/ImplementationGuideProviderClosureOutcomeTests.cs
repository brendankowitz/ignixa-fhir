// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Domain.Abstractions;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Packages;

public sealed class ImplementationGuideProviderClosureOutcomeTests
{
    private readonly IPackageLoader _loader = Substitute.For<IPackageLoader>();
    private readonly IPackageExtractor _extractor = Substitute.For<IPackageExtractor>();
    private readonly IPackageResourceImporter _importer = Substitute.For<IPackageResourceImporter>();
    private readonly IPackageResourceRepository _repository = Substitute.For<IPackageResourceRepository>();
    private readonly ImplementationGuideProvider _provider;

    public ImplementationGuideProviderClosureOutcomeTests()
    {
        _provider = new ImplementationGuideProvider(
            _loader,
            _extractor,
            _importer,
            _repository,
            NullLogger<ImplementationGuideProvider>.Instance);

        _importer.ImportAsync(Arg.Any<PackageExtractionResult>(), _repository, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var extraction = call.Arg<PackageExtractionResult>();
                return Task.FromResult(new PackageImportResult
                {
                    PackageId = extraction.Manifest.Name,
                    PackageVersion = extraction.Manifest.Version,
                    TotalResources = 1,
                    ImportedResources = 1,
                });
            });
        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    [Fact]
    public async Task GivenImportedAndAlreadyInstalledMembers_WhenLoadingWithDependencies_ThenStatusesAreReportedAndClosureIsComplete()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["dep"] = "1.0.0" });
        RegisterPackage("dep", "1.0.0");
        _repository.PackageVersionExistsAsync("dep", "1.0.0", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        result.ClosureMembers.ShouldContain(new PackageClosureMember("root", "1.0.0", PackageClosureMemberStatus.Imported));
        result.ClosureMembers.ShouldContain(new PackageClosureMember("dep", "1.0.0", PackageClosureMemberStatus.AlreadyInstalled));
        result.ClosureMembers.ShouldAllBe(member => !member.IsBlocking);
    }

    [Fact]
    public async Task GivenNewPackageWithNoResources_WhenLoadingWithDependencies_ThenMemberIsImported()
    {
        RegisterPackage("empty", "1.0.0");
        _importer.ImportAsync(Arg.Any<PackageExtractionResult>(), _repository, Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult
            {
                PackageId = "empty",
                PackageVersion = "1.0.0",
                TotalResources = 0,
                ImportedResources = 0,
            });

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "empty", "1.0.0", CancellationToken.None);

        result.ClosureMembers.ShouldContain(new PackageClosureMember("empty", "1.0.0", PackageClosureMemberStatus.Imported));
    }

    [Fact]
    public async Task GivenCoreDependency_WhenLoadingWithDependencies_ThenProvidedInProcessIsNotBlocking()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["hl7.fhir.r4.core"] = "4.0.1" });

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        var core = result.ClosureMembers.Single(member => member.PackageId == "hl7.fhir.r4.core");
        core.Status.ShouldBe(PackageClosureMemberStatus.ProvidedInProcess);
        core.IsBlocking.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenTransitiveLoadFailure_WhenLoadingWithDependencies_ThenMemberIsLoadFailedAndBlocking()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["broken"] = "2.0.0" });
        _loader.DownloadPackageAsync("broken", "2.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new InvalidOperationException("tarball missing"));

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        var broken = result.ClosureMembers.Single(member => member.PackageId == "broken");
        broken.Status.ShouldBe(PackageClosureMemberStatus.LoadFailed);
        broken.Reason.ShouldNotBeNull().ShouldContain("tarball missing");
        broken.IsBlocking.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenTransitiveDependencyLoadTimeout_WhenLoadingWithDependencies_ThenMemberIsLoadFailedAndBlocking()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["timed-out"] = "2.0.0" });
        _loader.DownloadPackageAsync("timed-out", "2.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new TaskCanceledException("package download timed out"));

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        var timedOut = result.ClosureMembers.Single(member => member.PackageId == "timed-out");
        timedOut.Status.ShouldBe(PackageClosureMemberStatus.LoadFailed);
        timedOut.IsBlocking.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenConflictingExactVersions_WhenLoadingWithDependencies_ThenConflictIsBlocking()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["A"] = "1.0.0", ["B"] = "1.0.0" });
        RegisterPackage("A", "1.0.0");
        RegisterPackage("B", "1.0.0", new Dictionary<string, string> { ["A"] = "2.0.0" });

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        var conflict = result.ClosureMembers.Single(member => member.Status == PackageClosureMemberStatus.VersionConflict);
        conflict.PackageId.ShouldBe("A");
        conflict.Version.ShouldBe("2.0.0");
        conflict.Reason.ShouldNotBeNull().ShouldContain("1.0.0");
        conflict.IsBlocking.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAlreadyInstalledManifestUnavailable_WhenLoadingWithDependencies_ThenManifestUnavailableIsBlocking()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["dep"] = "1.0.0" });
        _repository.PackageVersionExistsAsync("dep", "1.0.0", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _loader.DownloadPackageAsync("dep", "1.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new InvalidOperationException("tarball gone"));

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        result.ClosureMembers.ShouldContain(member =>
            member.PackageId == "dep" && member.Status == PackageClosureMemberStatus.ManifestUnavailable && member.IsBlocking);
    }

    [Fact]
    public async Task GivenAlreadyInstalledManifestRefetchTimeout_WhenLoadingWithDependencies_ThenManifestUnavailableIsBlocking()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["dep"] = "1.0.0" });
        _repository.PackageVersionExistsAsync("dep", "1.0.0", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _loader.DownloadPackageAsync("dep", "1.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new TaskCanceledException("manifest re-fetch timed out"));

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        var manifestUnavailable = result.ClosureMembers.Single(member =>
            member.PackageId == "dep" && member.Status == PackageClosureMemberStatus.ManifestUnavailable);
        manifestUnavailable.Status.ShouldBe(PackageClosureMemberStatus.ManifestUnavailable);
        manifestUnavailable.IsBlocking.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenCallerCancellationDuringManifestRefetch_WhenLoadingWithDependencies_ThenCancellationPropagates()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["dep"] = "1.0.0" });
        _repository.PackageVersionExistsAsync("dep", "1.0.0", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        _loader.DownloadPackageAsync("dep", "1.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new OperationCanceledException(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            () => _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", cancellation.Token));
    }

    private void RegisterPackage(string id, string version, IReadOnlyDictionary<string, string>? dependencies = null)
    {
        _loader.DownloadPackageAsync(id, version, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes($"mock-{id}@{version}"))));
        _extractor.ExtractAsync(Arg.Is<Stream>(stream => StreamMatches(stream, id, version)), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new PackageExtractionResult
            {
                Manifest = new PackageManifest
                {
                    Name = id,
                    Version = version,
                    FhirVersion = "4.0.1",
                    Dependencies = dependencies ?? System.Collections.Frozen.FrozenDictionary<string, string>.Empty,
                },
                Resources = Array.Empty<ExtractedResource>(),
            }));
    }

    private static bool StreamMatches(Stream stream, string id, string version)
    {
        if (stream is not MemoryStream memory)
        {
            return false;
        }

        var content = Encoding.UTF8.GetString(memory.ToArray());
        memory.Position = 0;
        return content == $"mock-{id}@{version}";
    }
}
