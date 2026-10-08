using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
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
        await cache.SetAsync("profiles:1", profileEntry, TimeSpan.FromMinutes(5), CancellationToken.None);
        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(new DefinitionsHandle(
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IFhirSchemaProvider>(),
                DefinitionsEventId: 11));
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

        (await cache.GetAsync("profiles:1", CancellationToken.None)).ShouldBeNull();
    }
}
