using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Metadata;

public class CapabilityStatementServiceTests
{
    [Fact]
    public async Task GivenCachedPackageProfiles_WhenTenantCapabilitiesInvalidate_ThenProfileDataIsRemoved()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryCapabilityCache(memoryCache);
        var profileEntry = new CapabilityCacheEntry(
            new CapabilityStatementJsonNode(),
            "profiles",
            DateTimeOffset.UtcNow);
        await cache.SetAsync("profiles:1:11:37", profileEntry, TimeSpan.FromMinutes(5), CancellationToken.None);
        await cache.SetAsync("profiles:1:11:38", profileEntry, TimeSpan.FromMinutes(5), CancellationToken.None);
        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(new DefinitionsHandle(
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IFhirSchemaProvider>(),
                DefinitionsEventId: 11,
                PublicationSequence: 37));
        var service = new CapabilityStatementService(
            [],
            cache,
            Substitute.For<ITenantConfigurationStore>(),
            versionContext,
            Substitute.For<IApplicationVersionInfo>(),
            NullLogger<CapabilityStatementService>.Instance);

        await service.InvalidateCacheAsync(
            new CapabilityContext(FhirVersion.R4, TenantId: 1),
            CancellationToken.None);

        (await cache.GetAsync("profiles:1:11:37", CancellationToken.None)).ShouldBeNull();
        (await cache.GetAsync("profiles:1:11:38", CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenPublishedDefinitions_WhenCapabilitiesAreCached_ThenThePublicationSequenceIsPartOfTheCacheKey()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryCapabilityCache(memoryCache);
        var tenant = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Capability tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "FileSystem" },
        };
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetTenantConfigurationAsync(1, CancellationToken.None).Returns(tenant);
        var schemaProvider = Substitute.For<IFhirSchemaProvider>();
        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(new DefinitionsHandle(
                Substitute.For<ISearchIndexer>(),
                schemaProvider,
                DefinitionsEventId: 11,
                PublicationSequence: 37));
        versionContext.GetBaseSchemaProvider(FhirVersion.R4).Returns(schemaProvider);
        var segment = Substitute.For<ICapabilitySegment>();
        segment.SegmentKey.Returns("test");
        segment.Priority.Returns(1);
        segment.ApplyAsync(
                Arg.Any<CapabilityStatementJsonNode>(),
                Arg.Any<CapabilityContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.ArgAt<CapabilityStatementJsonNode>(0).Rest.Add(new RestComponentJsonNode());
                return ValueTask.CompletedTask;
            });
        segment.GetVersionHashAsync(
                Arg.Any<CapabilityContext>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("test"));
        var service = new CapabilityStatementService(
            [segment],
            cache,
            tenantStore,
            versionContext,
            Substitute.For<IApplicationVersionInfo>(),
            NullLogger<CapabilityStatementService>.Instance);

        await service.GetCapabilityStatementAsync(
            new CapabilityContext(FhirVersion.R4, TenantId: 1),
            CancellationToken.None);

        (await cache.GetAsync("capability:R4:1:11:37", CancellationToken.None)).ShouldNotBeNull();
    }
}
