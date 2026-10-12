using System.Data.Common;
using Ignixa.Abstractions;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
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
        using var state = ConformanceEvents.State(ConformanceEvents.PackageActivated("first", "1"));
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
            state.ApplyAndTrack(ConformanceEvents.At(2, ConformanceEvents.PackageActivated("second", "1")));
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

    [Theory]
    [InlineData(false, new long[] { 1 })]
    [InlineData(true, new long[] { 1, 1 })]
    public async Task GivenCurrentGenerationAlreadyPublished_WhenRefreshedAgain_ThenOnlyAForcedRefreshRebuildsAndRepublishes(
        bool force,
        long[] expectedGenerations)
    {
        using var state = ConformanceEvents.State(ConformanceEvents.PackageActivated("first", "1"));
        var versions = new RecordingVersions();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.Tenants(CreateTenant(1)),
            versions: versions.Context);

        await refresher.RefreshAsync(force: false, CancellationToken.None);
        var generation = await refresher.RefreshAsync(force, CancellationToken.None);

        generation.ShouldBe(1);
        versions.Built.ShouldBe(expectedGenerations);
        versions.Published.ShouldBe(expectedGenerations);
    }

    [Fact]
    public async Task GivenFailedForcedRefresh_WhenTheNextUnforcedRefreshRuns_ThenItRebuildsAndPublishesTheCurrentGeneration()
    {
        using var state = ConformanceEvents.State(ConformanceEvents.PackageActivated("first", "1"));
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
        await refresher.RefreshAsync(force: false, CancellationToken.None);

        versions.Built.ShouldBe([1, 1]);
        versions.Published.ShouldBe([1, 1]);
        enumerations.ShouldBe(3);
    }

    [Fact]
    public async Task GivenTheCallerCancelsAtPublication_WhenRefreshCompletes_ThenCapabilitiesAreInvalidatedAfterwardsWithAnUncancelableToken()
    {
        using var state = ConformanceEvents.State(ConformanceEvents.PackageActivated("first", "1"));
        using var cancellation = new CancellationTokenSource();
        var versions = new RecordingVersions();
        var events = new List<string>();
        versions.Context
            .When(context => context.PublishConformanceDefinitionsSnapshot(
                Arg.Any<FhirVersion>(),
                Arg.Any<int>(),
                Arg.Any<ConformanceDefinitionsSnapshot>()))
            .Do(_ =>
            {
                events.Add("published");
                cancellation.Cancel();
            });
        var capabilities = Substitute.For<ICapabilityCacheInvalidator>();
        capabilities.InvalidateForTenantAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                events.Add(call.ArgAt<CancellationToken>(1).CanBeCanceled ? "invalidated (cancelable)" : "invalidated");
                return ValueTask.CompletedTask;
            });
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.Tenants(CreateTenant(1)),
            versions: versions.Context,
            capabilities: capabilities);

        await refresher.RefreshAsync(force: false, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));

        events.ShouldBe(["published", "invalidated"]);
    }

    [Theory]
    [InlineData(nameof(IOException), true)]
    [InlineData(nameof(TimeoutException), true)]
    [InlineData(nameof(DbException), true)]
    [InlineData(nameof(OperationCanceledException), false)]
    [InlineData(nameof(InvalidOperationException), false)]
    public async Task GivenConsumerFailure_WhenRefreshed_ThenOnlyDependencyFailuresAreMappedAndNothingIsPublished(
        string failureKind,
        bool expectMapped)
    {
        using var state = ConformanceEvents.State(ConformanceEvents.PackageActivated("first", "1"));
        Exception failure = failureKind switch
        {
            nameof(IOException) => new IOException("Store unavailable."),
            nameof(TimeoutException) => new TimeoutException("Catalog write timed out."),
            nameof(DbException) => new TestDbException(),
            nameof(OperationCanceledException) => new OperationCanceledException("Independent cancellation."),
            _ => new InvalidOperationException("Programmer error."),
        };
        var versions = new RecordingVersions();
        var catalog = Substitute.For<ISearchParameterCatalogSynchronizer>();
        catalog.SynchronizeAsync(
                Arg.Any<TenantConfiguration>(),
                Arg.Any<ISearchParameterDefinitionManager>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));
        var capabilities = Substitute.For<ICapabilityCacheInvalidator>();
        using var refresher = TestConformanceRefresher.Create(
            state,
            tenants: TestConformanceRefresher.Tenants(CreateTenant(1)),
            versions: versions.Context,
            catalog: catalog,
            capabilities: capabilities);

        var exception = await Record.ExceptionAsync(() =>
            refresher.RefreshAsync(force: false, CancellationToken.None));

        if (expectMapped)
        {
            exception.ShouldBeOfType<ConformanceConsumerRefreshException>().InnerException.ShouldBeSameAs(failure);
        }
        else
        {
            exception.ShouldBeSameAs(failure);
        }

        versions.Published.ShouldBeEmpty();
        await capabilities.DidNotReceiveWithAnyArgs().InvalidateForTenantAsync(default, default);
    }

    [Fact]
    public async Task GivenMultipleTenants_WhenRefreshed_ThenEachTenantsDefinitionsAreCatalogedBeforePublication()
    {
        using var state = ConformanceEvents.State(ConformanceEvents.PackageActivated("first", "1"));
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
    }

    [Fact]
    public async Task GivenUnseenDurableEvents_WhenSynchronized_ThenTheyAreAppliedAndTheirGenerationIsPublished()
    {
        using var state = new ConformanceState();
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.ReadFromAsync(0, Arg.Any<CancellationToken>())
            .Returns(_ => Events(ConformanceEvents.At(7, ConformanceEvents.PackageActivated("remote", "1"))));
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

    private sealed class TestDbException() : DbException("Database unavailable.");
}
