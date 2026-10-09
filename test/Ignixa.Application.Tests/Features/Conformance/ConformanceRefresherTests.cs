using Ignixa.Abstractions;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ConformanceRefresherTests
{
    [Fact]
    public async Task GivenStateAdvancesWhileDefinitionsBuild_WhenRefreshPublishes_ThenItDiscardsTheStaleGeneration()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        var versions = new RecordingVersions();
        var firstEnumerationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstEnumeration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerations = 0;
        var tenants = Substitute.For<Ignixa.Domain.Abstractions.ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ++enumerations == 1
                ? new ValueTask<IReadOnlyList<TenantConfiguration>>(BlockAsync())
                : new ValueTask<IReadOnlyList<TenantConfiguration>>([CreateTenant(1)]));
        var capabilities = Substitute.For<ICapabilityCacheInvalidator>();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: tenants,
            versions: versions.Context,
            capabilities: capabilities);

        var refresh = refresher.RefreshAsync(force: false, CancellationToken.None);
        await firstEnumerationStarted.Task;
        using (await state.AcquireActivationLockAsync(CancellationToken.None))
        {
            state.ApplyAndTrack(CreatePackageEvent(2, "second"));
        }

        releaseFirstEnumeration.TrySetResult();
        var publishedGeneration = await refresh;

        publishedGeneration.ShouldBe(2);
        versions.Built.ShouldBe([1, 2]);
        versions.Published.ShouldBe([2]);
        await capabilities.Received(1).InvalidateForTenantAsync(1, CancellationToken.None);

        async Task<IReadOnlyList<TenantConfiguration>> BlockAsync()
        {
            firstEnumerationStarted.TrySetResult();
            await releaseFirstEnumeration.Task;
            return [CreateTenant(1)];
        }
    }

    [Fact]
    public async Task GivenCurrentGenerationAlreadyPublished_WhenRefreshIsNotForced_ThenNothingIsRebuilt()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        var versions = new RecordingVersions();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.Tenants(CreateTenant(1)),
            versions: versions.Context);

        await refresher.RefreshAsync(force: false, CancellationToken.None);
        var generation = await refresher.RefreshAsync(force: false, CancellationToken.None);

        generation.ShouldBe(1);
        versions.Built.ShouldBe([1]);
        versions.Published.ShouldBe([1]);
    }

    [Fact]
    public async Task GivenCurrentGenerationAlreadyPublished_WhenRefreshIsForced_ThenSameGenerationIsRebuiltAndPublished()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        var versions = new RecordingVersions();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.Tenants(CreateTenant(1)),
            versions: versions.Context);

        await refresher.RefreshAsync(force: false, CancellationToken.None);
        await refresher.RefreshAsync(force: true, CancellationToken.None);

        versions.Built.ShouldBe([1, 1]);
        versions.Published.ShouldBe([1, 1]);
    }

    [Fact]
    public async Task GivenFailedForcedRefresh_WhenTheNextUnforcedRefreshRuns_ThenItRebuildsAndPublishesTheCurrentGeneration()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        var versions = new RecordingVersions();
        var enumerations = 0;
        var tenants = Substitute.For<Ignixa.Domain.Abstractions.ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ++enumerations == 2
                ? ValueTask.FromException<IReadOnlyList<TenantConfiguration>>(new IOException("Store unavailable."))
                : new ValueTask<IReadOnlyList<TenantConfiguration>>([CreateTenant(1)]));
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: tenants,
            versions: versions.Context);

        await refresher.RefreshAsync(force: false, CancellationToken.None);
        await Should.ThrowAsync<ConformanceConsumerRefreshException>(() =>
            refresher.RefreshAsync(force: true, CancellationToken.None));
        var pendingAfterFailure = refresher.HasPendingRefresh;
        await refresher.RefreshAsync(force: false, CancellationToken.None);

        pendingAfterFailure.ShouldBeTrue();
        refresher.HasPendingRefresh.ShouldBeFalse();
        versions.Built.ShouldBe([1, 1]);
        versions.Published.ShouldBe([1, 1]);
        enumerations.ShouldBe(3);
    }

    [Fact]
    public async Task GivenUnforcedRefreshFails_WhenRefreshedAgain_ThenNoPendingForceIsRecorded()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.FailingTenants(new IOException("Store unavailable.")));

        await Should.ThrowAsync<ConformanceConsumerRefreshException>(() =>
            refresher.RefreshAsync(force: false, CancellationToken.None));

        refresher.HasPendingRefresh.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenPublishedDefinitions_WhenRefreshCompletes_ThenCapabilitiesAreInvalidatedAfterPublicationAndActivationLockRelease()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        var versions = new RecordingVersions();
        var events = new List<string>();
        versions.Context
            .When(context => context.PublishConformanceDefinitionsSnapshot(
                Arg.Any<FhirVersion>(),
                Arg.Any<int>(),
                Arg.Any<ConformanceDefinitionsSnapshot>()))
            .Do(_ => events.Add("published"));
        var capabilities = Substitute.For<ICapabilityCacheInvalidator>();
        capabilities.InvalidateForTenantAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask(InvalidateAsync()));
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.Tenants(CreateTenant(1)),
            versions: versions.Context,
            capabilities: capabilities);

        await refresher.RefreshAsync(force: false, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        events.ShouldBe(["published", "invalidated"]);

        async Task InvalidateAsync()
        {
            using (await state.AcquireActivationLockAsync(CancellationToken.None))
            {
            }

            events.Add("invalidated");
        }
    }

    [Fact]
    public async Task GivenCallerCancellationAfterPublication_WhenCapabilitiesAreInvalidated_ThenItUsesAnUncancelableToken()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        using var cancellation = new CancellationTokenSource();
        var versions = new RecordingVersions();
        versions.Context
            .When(context => context.PublishConformanceDefinitionsSnapshot(
                Arg.Any<FhirVersion>(),
                Arg.Any<int>(),
                Arg.Any<ConformanceDefinitionsSnapshot>()))
            .Do(_ => cancellation.Cancel());
        var capabilities = Substitute.For<ICapabilityCacheInvalidator>();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.Tenants(CreateTenant(1)),
            versions: versions.Context,
            capabilities: capabilities);

        await refresher.RefreshAsync(force: false, cancellation.Token);

        await capabilities.Received(1).InvalidateForTenantAsync(1, CancellationToken.None);
        await capabilities.DidNotReceive().InvalidateForTenantAsync(1, cancellation.Token);
    }

    [Fact]
    public async Task GivenRecoverableTenantConfigurationFailure_WhenRefreshed_ThenItThrowsTheDedicatedRefreshException()
    {
        using var state = new ConformanceState();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.FailingTenants(new IOException("Configuration store unavailable.")));

        var exception = await Should.ThrowAsync<ConformanceConsumerRefreshException>(() =>
            refresher.RefreshAsync(force: false, CancellationToken.None));

        exception.InnerException.ShouldBeOfType<IOException>();
    }

    [Fact]
    public async Task GivenIndependentCancellation_WhenRefreshed_ThenItPropagatesTheCancellation()
    {
        using var state = new ConformanceState();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.FailingTenants(new OperationCanceledException("Independent cancellation.")));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            refresher.RefreshAsync(force: false, CancellationToken.None));
    }

    [Fact]
    public async Task GivenCatalogSynchronizationTimesOut_WhenRefreshed_ThenNothingIsPublishedOrInvalidated()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        var versions = new RecordingVersions();
        var catalog = Substitute.For<ISearchParameterCatalogSynchronizer>();
        catalog.SynchronizeAsync(
                Arg.Any<TenantConfiguration>(),
                Arg.Any<ISearchParameterDefinitionManager>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TimeoutException("Catalog write timed out.")));
        var capabilities = Substitute.For<ICapabilityCacheInvalidator>();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.Tenants(CreateTenant(1)),
            versions: versions.Context,
            catalog: catalog,
            capabilities: capabilities);

        var exception = await Should.ThrowAsync<ConformanceConsumerRefreshException>(() =>
            refresher.RefreshAsync(force: false, CancellationToken.None));

        exception.InnerException.ShouldBeOfType<TimeoutException>();
        versions.Published.ShouldBeEmpty();
        await capabilities.DidNotReceiveWithAnyArgs().InvalidateForTenantAsync(default, default);
    }

    [Fact]
    public async Task GivenMultipleTenants_WhenRefreshed_ThenEachTenantsExtractionDefinitionsAreCatalogedBeforePublication()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        var versions = new RecordingVersions();
        var catalog = Substitute.For<ISearchParameterCatalogSynchronizer>();
        var catalogedBeforePublication = new List<int>();
        catalog.SynchronizeAsync(
                Arg.Any<TenantConfiguration>(),
                Arg.Any<ISearchParameterDefinitionManager>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (versions.Published.Count == 0)
                {
                    catalogedBeforePublication.Add(call.Arg<TenantConfiguration>().TenantId);
                }

                call.Arg<ISearchParameterDefinitionManager>().ShouldBeSameAs(versions.Definitions);
                return Task.CompletedTask;
            });
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.Tenants(CreateTenant(1), CreateTenant(2)),
            versions: versions.Context,
            catalog: catalog);

        await refresher.RefreshAsync(force: false, CancellationToken.None);

        catalogedBeforePublication.ShouldBe([1, 2]);
        versions.Published.ShouldBe([1, 1]);
    }

    [Fact]
    public async Task GivenMultipleTenants_WhenPackageIsUnloaded_ThenItInvalidatesEveryPublishedTenantsCapabilities()
    {
        using var state = new ConformanceState();
        var versions = new RecordingVersions();
        var capabilities = Substitute.For<ICapabilityCacheInvalidator>();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.Tenants(CreateTenant(1), CreateTenant(2)),
            versions: versions.Context,
            capabilities: capabilities);
        var handler = new PackageUnloadedNotificationHandler(
            refresher,
            NullLogger<PackageUnloadedNotificationHandler>.Instance);

        await handler.HandleAsync(
            new PackageUnloadedEvent("example.package", "1.0.0", 1, DateTimeOffset.UtcNow),
            CancellationToken.None);

        await capabilities.Received(1).InvalidateForTenantAsync(1, CancellationToken.None);
        await capabilities.Received(1).InvalidateForTenantAsync(2, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenRefreshFails_WhenPackageNotificationIsHandled_ThenTheDurableOperationDoesNotThrow(bool unload)
    {
        using var state = new ConformanceState();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.FailingTenants(new IOException("Store unavailable.")));

        if (unload)
        {
            var handler = new PackageUnloadedNotificationHandler(
                refresher,
                NullLogger<PackageUnloadedNotificationHandler>.Instance);
            await Should.NotThrowAsync(() => handler.HandleAsync(
                new PackageUnloadedEvent("test", "1", 1, DateTimeOffset.UtcNow),
                CancellationToken.None));
        }
        else
        {
            var handler = new PackageLoadedNotificationHandler(
                refresher,
                NullLogger<PackageLoadedNotificationHandler>.Instance);
            await Should.NotThrowAsync(() => handler.HandleAsync(
                new PackageLoadedEvent("test", "1", 1, DateTimeOffset.UtcNow),
                CancellationToken.None));
        }

        refresher.HasPendingRefresh.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenUnseenDurableEvents_WhenSynchronized_ThenTheyAreAppliedAndTheirGenerationIsPublished()
    {
        using var state = new ConformanceState();
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.ReadFromAsync(0, Arg.Any<CancellationToken>())
            .Returns(_ => Events(CreatePackageEvent(7, "remote")));
        var versions = new RecordingVersions();
        using var refresher = TestConformanceRefresher.Create(
            state,
            eventStore,
            TestConformanceRefresher.Tenants(CreateTenant(1)),
            versions.Context);

        await refresher.SynchronizeAsync(CancellationToken.None);

        state.LastProcessedEventId.ShouldBe(7);
        versions.Published.ShouldBe([7]);
    }

    private static SourceEvent CreatePackageEvent(long eventId, string packageId) =>
        new(
            eventId,
            $"package:{packageId}@1",
            nameof(PackageActivated),
            new PackageActivated(packageId, "1", []),
            DateTimeOffset.UtcNow);

    private static TenantConfiguration CreateTenant(int tenantId) => new()
    {
        TenantId = tenantId,
        DisplayName = $"Tenant {tenantId}",
        FhirVersion = "4.0",
        Storage = new TenantStorageConfiguration { Type = "FileSystem" },
    };

    private static async IAsyncEnumerable<SourceEvent> Events(params SourceEvent[] events)
    {
        foreach (var sourceEvent in events)
        {
            yield return sourceEvent;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// A substituted version context that records which generations were built and published.
    /// </summary>
    private sealed class RecordingVersions
    {
        public RecordingVersions()
        {
            Definitions.AllSearchParameters.Returns([]);
            Context.CreateConformanceDefinitionsSnapshot(
                    Arg.Any<FhirVersion>(),
                    Arg.Any<int>(),
                    Arg.Any<ConformanceStateSnapshot>(),
                    Arg.Any<long>())
                .Returns(call =>
                {
                    var generation = call.ArgAt<long>(3);
                    Built.Add(generation);
                    return new ConformanceDefinitionsSnapshot(
                        Definitions,
                        Definitions,
                        new DefinitionsHandle(
                            Substitute.For<ISearchIndexer>(),
                            Substitute.For<IFhirSchemaProvider>(),
                            generation),
                        call.ArgAt<ConformanceStateSnapshot>(2));
                });
            Context
                .When(context => context.PublishConformanceDefinitionsSnapshot(
                    Arg.Any<FhirVersion>(),
                    Arg.Any<int>(),
                    Arg.Any<ConformanceDefinitionsSnapshot>()))
                .Do(call => Published.Add(call.Arg<ConformanceDefinitionsSnapshot>().Generation));
        }

        public IFhirVersionContext Context { get; } = Substitute.For<IFhirVersionContext>();
        public ISearchParameterDefinitionManager Definitions { get; } = Substitute.For<ISearchParameterDefinitionManager>();

        // Built records one entry per tenant per rebuild; tests with one tenant read it as generations.
        public List<long> Built { get; } = [];
        public List<long> Published { get; } = [];
    }
}
