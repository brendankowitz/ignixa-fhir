using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests;

/// <summary>
/// Builds a real <see cref="ConformanceRefresher"/> whose I/O boundaries are substitutes. With the defaults it
/// has no tenants, so a refresh publishes nothing and only advances the published generation.
/// </summary>
internal static class TestConformanceRefresher
{
    public static ConformanceRefresher Create(
        ConformanceState state,
        ISourceEventStore? eventStore = null,
        ITenantConfigurationStore? tenants = null,
        IFhirVersionContext? versions = null,
        ISearchParameterCatalogSynchronizer? catalog = null,
        ICapabilityCacheInvalidator? capabilities = null) =>
        new(
            state,
            eventStore ?? EmptyEventStore(),
            versions ?? Substitute.For<IFhirVersionContext>(),
            tenants ?? Tenants(),
            catalog ?? Substitute.For<ISearchParameterCatalogSynchronizer>(),
            capabilities ?? Substitute.For<ICapabilityCacheInvalidator>(),
            NullLogger<ConformanceRefresher>.Instance);

    public static ITenantConfigurationStore Tenants(params TenantConfiguration[] tenants)
    {
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IReadOnlyList<TenantConfiguration>>(tenants));
        return store;
    }

    /// <summary>A tenant store whose enumeration, the first step of every rebuild, fails.</summary>
    public static ITenantConfigurationStore FailingTenants(Exception failure)
    {
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException<IReadOnlyList<TenantConfiguration>>(failure));
        return store;
    }

    public static ISourceEventStore EmptyEventStore()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(_ => NoEvents());
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => NoEvents());
        return store;
    }

    private static async IAsyncEnumerable<SourceEvent> NoEvents()
    {
        await Task.CompletedTask;
        yield break;
    }
}
