using System.Text.Json.Nodes;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Experimental.Mcp.Tools.PackageManagement;
using Ignixa.Application.Infrastructure;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.Mcp.Tools.PackageManagement;

public class InstallPackageToolTests
{
    private const string PackageId = "test.package";
    private const string Version = "1.0.0";

    [Fact]
    public async Task GivenStoredPackageIsRejectedByActivation_WhenInstalled_ThenTheToolReturnsAFailureNamingTheIssue()
    {
        using var state = new ConformanceState();
        var tool = CreateTool(state, ["Patient", "Practitioner"]);

        var result = await tool.InstallPackageAsync(PackageId, Version, tenantId: 1, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem().ShouldStartWith("error SP_MIXED_BASE_SHADOW:");
        result.Message.ShouldContain("did not activate");
        result.Message.ShouldContain("SP_MIXED_BASE_SHADOW");
        result.PendingReindex.ShouldBeEmpty();
        state.Packages.ShouldNotContainKey($"{PackageId}@{Version}");
    }

    [Fact]
    public async Task GivenActivatablePackage_WhenInstalled_ThenTheToolReportsSuccessWithItsPendingReindex()
    {
        using var state = new ConformanceState();
        var tool = CreateTool(state, ["Patient"]);

        var result = await tool.InstallPackageAsync(PackageId, Version, tenantId: 1, CancellationToken.None);

        result.Success.ShouldBeTrue(string.Join("; ", result.Issues));
        result.PendingReindex.ShouldBe(["Patient"]);
        state.Packages.ShouldContainKey($"{PackageId}@{Version}");
    }

    private static InstallPackageTool CreateTool(ConformanceState state, string[] baseTypes)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(
                new TenantConfiguration { TenantId = 1, DisplayName = "One", FhirVersion = "4.0" }));
        var provider = Substitute.For<IImplementationGuideProvider>();
        provider.LoadPackageAsync("1", PackageId, Version, Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult { PackageId = PackageId, PackageVersion = Version, ImportedResources = 1 });
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetResourcesForActivationAsync(PackageId, Version, Arg.Any<CancellationToken>())
            .Returns([CreateParameter(baseTypes)]);
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.AppendAsync(Arg.Any<IEnumerable<NewSourceEvent>>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<SourceEvent>>(call.Arg<IEnumerable<NewSourceEvent>>()
                .Select((sourceEvent, index) => new SourceEvent(
                    index + 1,
                    sourceEvent.StreamId,
                    sourceEvent.EventType,
                    sourceEvent.Data,
                    DateTimeOffset.UtcNow))
                .ToArray()));
        return new InstallPackageTool(
            Substitute.For<IFhirRequestContextAccessor>(),
            tenants,
            provider,
            Substitute.For<INpmPackageSearchService>(),
            TestPackageActivationPipeline.Create(repository, eventStore, state));
    }

    private static PackageResource CreateParameter(string[] baseTypes) =>
        new()
        {
            PackageId = PackageId,
            PackageVersion = Version,
            ResourceType = "SearchParameter",
            ResourceId = "identifier",
            Canonical = "http://example.org/SearchParameter/identifier",
            FhirVersion = "4.0.1",
            ResourceJson = new JsonObject
            {
                ["resourceType"] = "SearchParameter",
                ["id"] = "identifier",
                ["url"] = "http://example.org/SearchParameter/identifier",
                ["code"] = baseTypes.Length > 1 ? "identifier" : "custom-identifier",
                ["base"] = new JsonArray(baseTypes.Select(type => JsonValue.Create(type)).ToArray<JsonNode?>()),
                ["type"] = "token",
                ["expression"] = "Resource.identifier",
            }.ToJsonString(),
        };
}
