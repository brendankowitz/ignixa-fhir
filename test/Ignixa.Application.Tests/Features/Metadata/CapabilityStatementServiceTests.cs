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

/// <summary>F9: an unload republishes the same generation, so tenant capabilities are cached by publication as well as generation.</summary>
public sealed class CapabilityStatementServiceTests : IDisposable
{
    private static readonly CapabilityContext Tenant1 = new(FhirVersion.R4, TenantId: 1);
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly MemoryCapabilityCache _cache;
    private long _publicationSequence = 37;

    public CapabilityStatementServiceTests() => _cache = new MemoryCapabilityCache(_memoryCache);

    [Fact]
    public async Task GivenCachedPackageProfiles_WhenTenantCapabilitiesInvalidate_ThenProfileDataIsRemoved()
    {
        var profileEntry = new CapabilityCacheEntry(new CapabilityStatementJsonNode(), "profiles", DateTimeOffset.UtcNow);
        await _cache.SetAsync("profiles:1:11:37", profileEntry, TimeSpan.FromMinutes(5), CancellationToken.None);
        await _cache.SetAsync("profiles:1:11:38", profileEntry, TimeSpan.FromMinutes(5), CancellationToken.None);

        await CreateService().InvalidateCacheAsync(Tenant1, CancellationToken.None);

        (await _cache.GetAsync("profiles:1:11:37", CancellationToken.None)).ShouldBeNull();
        (await _cache.GetAsync("profiles:1:11:38", CancellationToken.None)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(37, 1)]
    [InlineData(38, 2)]
    public async Task GivenCachedCapabilities_WhenTheSameGenerationIsRepublished_ThenOnlyANewPublicationRebuildsThem(
        long secondPublicationSequence,
        int expectedBuilds)
    {
        var segment = Substitute.For<ICapabilitySegment>();
        segment.SegmentKey.Returns("test");
        segment.Priority.Returns(1);
        segment.GetVersionHashAsync(Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult("test"));
        segment.ApplyAsync(Arg.Any<CapabilityStatementJsonNode>(), Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.ArgAt<CapabilityStatementJsonNode>(0).Rest.Add(new RestComponentJsonNode());
                return ValueTask.CompletedTask;
            });
        var service = CreateService(segment);

        await service.GetCapabilityStatementAsync(Tenant1, CancellationToken.None);
        _publicationSequence = secondPublicationSequence;
        await service.GetCapabilityStatementAsync(Tenant1, CancellationToken.None);

        segment.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ICapabilitySegment.ApplyAsync)).ShouldBe(expectedBuilds);
    }

    public void Dispose() => _memoryCache.Dispose();

    // Tenant 1's definitions at generation 11, at whatever publication sequence is current.
    private CapabilityStatementService CreateService(params ICapabilitySegment[] segments)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, CancellationToken.None)
            .Returns(new TenantConfiguration { TenantId = 1, DisplayName = "Capability tenant", FhirVersion = "4.0" });
        var schemaProvider = Substitute.For<IFhirSchemaProvider>();
        var indexer = Substitute.For<ISearchIndexer>();
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(_ => new DefinitionsHandle(indexer, schemaProvider, DefinitionsEventId: 11, PublicationSequence: _publicationSequence));
        versions.GetBaseSchemaProvider(FhirVersion.R4).Returns(schemaProvider);
        return new(segments, _cache, tenants, versions, Substitute.For<IApplicationVersionInfo>(), NullLogger<CapabilityStatementService>.Instance);
    }
}
