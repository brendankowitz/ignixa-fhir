using System.Data.Common;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Ignixa.Application.Tests.BackgroundOperations.Reindex;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>
/// Spec §5.4: a refresh builds each tenant's definitions, catalogs them, publishes them all, then invalidates capabilities.
/// Each test asserts that ordered effect timeline.
/// </summary>
public class ConformanceRefresherTests
{
    private static readonly string[] OneRefresh = ["built 1@1", "cataloged 1", "published 1@1", "invalidated 1"];

    [Fact]
    public async Task GivenStateAdvancesWhileDefinitionsBuild_WhenRefreshPublishes_ThenItDiscardsTheStaleGeneration()
    {
        var enumerationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEnumeration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerations = 0;
        using var timeline = new Timeline();
        timeline.Tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ++enumerations == 1 ? new ValueTask<IReadOnlyList<TenantConfiguration>>(BlockAsync()) : new([Tenant(1)]));
        using var refresher = timeline.Refresher();

        var refresh = refresher.RefreshAsync(force: false, CancellationToken.None);
        await enumerationStarted.Task;
        using (await timeline.State.AcquireActivationLockAsync(CancellationToken.None))
        {
            timeline.State.ApplyAndTrack(At(2, PackageActivated("second", "1")));
        }

        releaseEnumeration.TrySetResult();

        (await refresh).ShouldBe(2);
        timeline.Events.ShouldBe(["built 1@1", "cataloged 1", "built 1@2", "cataloged 1", "published 1@2", "invalidated 1"]);

        async Task<IReadOnlyList<TenantConfiguration>> BlockAsync()
        {
            enumerationStarted.TrySetResult();
            await releaseEnumeration.Task;
            return [Tenant(1)];
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task GivenCurrentGenerationAlreadyPublished_WhenRefreshedAgain_ThenOnlyAForcedRefreshRebuildsAndRepublishes(
        bool force,
        int expectedRefreshes)
    {
        using var timeline = new Timeline(Tenant(1));
        using var refresher = timeline.Refresher();

        await refresher.RefreshAsync(force: false, CancellationToken.None);
        (await refresher.RefreshAsync(force, CancellationToken.None)).ShouldBe(1);

        timeline.Events.ShouldBe(Enumerable.Repeat(OneRefresh, expectedRefreshes).SelectMany(refresh => refresh));
    }

    [Fact]
    public async Task GivenFailedForcedRefresh_WhenTheNextUnforcedRefreshRuns_ThenItRebuildsAndPublishesTheCurrentGeneration()
    {
        var enumerations = 0;
        using var timeline = new Timeline();
        timeline.Tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ++enumerations == 2
                ? ValueTask.FromException<IReadOnlyList<TenantConfiguration>>(new IOException("Store unavailable."))
                : new([Tenant(1)]));
        using var refresher = timeline.Refresher();

        await refresher.RefreshAsync(force: false, CancellationToken.None);
        await Should.ThrowAsync<ConformanceConsumerRefreshException>(() => refresher.RefreshAsync(force: true, CancellationToken.None));
        await refresher.RefreshAsync(force: false, CancellationToken.None);

        timeline.Events.ShouldBe([.. OneRefresh, .. OneRefresh]);
    }

    // Published definitions are visible, so the caller's cancellation must not skip the invalidation that follows.
    [Fact]
    public async Task GivenTwoTenantsAndTheCallerCancelsAtPublication_WhenRefreshed_ThenEachIsCatalogedBeforePublicationAndInvalidatedAfterUncancelably()
    {
        using var cancellation = new CancellationTokenSource();
        using var timeline = new Timeline(Tenant(1), Tenant(2)) { OnPublish = cancellation.Cancel };
        using var refresher = timeline.Refresher();

        await refresher.RefreshAsync(force: false, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));

        timeline.Events.ShouldBe(
            ["built 1@1", "cataloged 1", "built 2@1", "cataloged 2", "published 1@1", "published 2@1", "invalidated 1", "invalidated 2"]);
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
        Exception failure = failureKind switch
        {
            nameof(IOException) => new IOException("Store unavailable."),
            nameof(TimeoutException) => new TimeoutException("Catalog write timed out."),
            nameof(DbException) => new TestDbException(),
            nameof(OperationCanceledException) => new OperationCanceledException("Independent cancellation."),
            _ => new InvalidOperationException("Programmer error."),
        };
        using var timeline = new Timeline(Tenant(1)) { CatalogFailure = failure };
        using var refresher = timeline.Refresher();

        var exception = await Record.ExceptionAsync(() => refresher.RefreshAsync(force: false, CancellationToken.None));

        (expectMapped ? exception.ShouldBeOfType<ConformanceConsumerRefreshException>().InnerException : exception)
            .ShouldBeSameAs(failure);
        timeline.Events.ShouldBe(["built 1@1"]);
    }

    // F9: an unload republishes the current generation, so every tenant's capabilities are rebuilt.
    [Fact]
    public async Task GivenMultipleTenants_WhenPackageIsUnloaded_ThenItInvalidatesEveryPublishedTenantsCapabilities()
    {
        using var timeline = new Timeline(Tenant(1), Tenant(2));
        using var refresher = timeline.Refresher();
        await refresher.RefreshAsync(force: false, CancellationToken.None);
        var handler = new PackageUnloadedNotificationHandler(refresher, NullLogger<PackageUnloadedNotificationHandler>.Instance);

        await handler.HandleAsync(new PackageUnloadedEvent("example.package", "1.0.0", 1, DateTimeOffset.UtcNow), CancellationToken.None);

        timeline.Events.Count(entry => entry == "invalidated 1").ShouldBe(2);
        timeline.Events.Count(entry => entry == "invalidated 2").ShouldBe(2);
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

        var handle = unload
            ? new PackageUnloadedNotificationHandler(refresher, NullLogger<PackageUnloadedNotificationHandler>.Instance)
                .HandleAsync(new PackageUnloadedEvent("test", "1", 1, DateTimeOffset.UtcNow), CancellationToken.None)
            : new PackageLoadedNotificationHandler(refresher, NullLogger<PackageLoadedNotificationHandler>.Instance)
                .HandleAsync(new PackageLoadedEvent("test", "1", 1, DateTimeOffset.UtcNow), CancellationToken.None);

        await Should.NotThrowAsync(() => handle);
    }

    [Fact]
    public async Task GivenUnseenDurableEvents_WhenSynchronized_ThenTheyAreAppliedAndTheirGenerationIsPublished()
    {
        using var timeline = new Timeline(Tenant(1));
        var log = new InMemoryEventLog();
        log.Events.AddRange([At(1, PackageActivated("first", "1")), At(7, PackageActivated("remote", "1"))]);
        using var refresher = timeline.Refresher(log);

        await refresher.SynchronizeAsync(CancellationToken.None);

        timeline.State.LastProcessedEventId.ShouldBe(7);
        timeline.Events.ShouldContain("published 1@7");
    }

    private static TenantConfiguration Tenant(int tenantId) => ReindexTestHelper.Tenant(tenantId, "FileSystem");

    /// <summary>
    /// A refresher over substituted consumers that record, in order, "built T@G", "cataloged T",
    /// "published T@G" and "invalidated T" (suffixed "(cancelable)" if given a cancelable token).
    /// </summary>
    private sealed class Timeline : IDisposable
    {
        private readonly ISearchParameterDefinitionManager _definitions = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ISearchParameterCatalogSynchronizer _catalog = Substitute.For<ISearchParameterCatalogSynchronizer>();

        public Timeline(params TenantConfiguration[] tenants)
        {
            Tenants = TestConformanceRefresher.Tenants(tenants);
            _catalog.SynchronizeAsync(Arg.Any<TenantConfiguration>(), Arg.Any<ISearchParameterDefinitionManager>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    if (CatalogFailure is not null)
                    {
                        return Task.FromException(CatalogFailure);
                    }

                    call.Arg<ISearchParameterDefinitionManager>().ShouldBeSameAs(_definitions);
                    Events.Add($"cataloged {call.Arg<TenantConfiguration>().TenantId}");
                    return Task.CompletedTask;
                });
        }

        public ConformanceState State { get; } = ConformanceEvents.State(PackageActivated("first", "1"));

        public ITenantConfigurationStore Tenants { get; }

        public List<string> Events { get; } = [];

        public Action? OnPublish { get; init; }

        public Exception? CatalogFailure { get; init; }

        public ConformanceRefresher Refresher(ISourceEventStore? eventStore = null) =>
            TestConformanceRefresher.Create(
                State,
                eventStore,
                Tenants,
                TestConformanceRefresher.RecordingVersions(Events, _definitions, () => OnPublish?.Invoke()),
                _catalog,
                TestConformanceRefresher.RecordingCapabilities(Events));

        public void Dispose() => State.Dispose();
    }

    private sealed class TestDbException() : DbException("Database unavailable.");
}
