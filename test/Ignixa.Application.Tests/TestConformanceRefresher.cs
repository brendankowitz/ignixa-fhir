using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
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
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(_ => Stream());
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => Stream());
        return store;
    }

    /// <summary>
    /// A version context whose snapshots wrap <paramref name="definitions"/>; it records "built T@G" and
    /// "published T@G" in <paramref name="effects"/>, then runs <paramref name="onPublish"/>.
    /// </summary>
    public static IFhirVersionContext RecordingVersions(
        List<string> effects,
        ISearchParameterDefinitionManager? definitions = null,
        Action? onPublish = null)
    {
        definitions ??= Substitute.For<ISearchParameterDefinitionManager>();
        var versions = Substitute.For<IFhirVersionContext>();
        versions.CreateConformanceDefinitionsSnapshot(Arg.Any<FhirVersion>(), Arg.Any<int>(), Arg.Any<ConformanceStateSnapshot>(), Arg.Any<long>())
            .Returns(call =>
            {
                var generation = call.ArgAt<long>(3);
                effects.Add($"built {call.ArgAt<int>(1)}@{generation}");
                var handle = new DefinitionsHandle(Substitute.For<ISearchIndexer>(), Substitute.For<IFhirSchemaProvider>(), generation);
                return new ConformanceDefinitionsSnapshot(definitions, handle, call.ArgAt<ConformanceStateSnapshot>(2));
            });
        versions.When(context => context.PublishConformanceDefinitionsSnapshot(Arg.Any<FhirVersion>(), Arg.Any<int>(), Arg.Any<ConformanceDefinitionsSnapshot>()))
            .Do(call =>
            {
                effects.Add($"published {call.ArgAt<int>(1)}@{call.Arg<ConformanceDefinitionsSnapshot>().Generation}");
                onPublish?.Invoke();
            });
        return versions;
    }

    /// <summary>Records "invalidated T", suffixed " (cancelable)" when given a cancelable token, in <paramref name="effects"/>.</summary>
    public static ICapabilityCacheInvalidator RecordingCapabilities(List<string> effects)
    {
        var capabilities = Substitute.For<ICapabilityCacheInvalidator>();
        capabilities.InvalidateForTenantAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                effects.Add($"invalidated {call.ArgAt<int>(0)}{(call.ArgAt<CancellationToken>(1).CanBeCanceled ? " (cancelable)" : string.Empty)}");
                return ValueTask.CompletedTask;
            });
        return capabilities;
    }

    /// <summary>A stored event stream, for event store substitutes.</summary>
    public static async IAsyncEnumerable<SourceEvent> Stream(params SourceEvent[] events)
    {
        await Task.CompletedTask;
        foreach (var sourceEvent in events)
        {
            yield return sourceEvent;
        }
    }
}
