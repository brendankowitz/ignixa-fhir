using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ConformanceRefreshPublisherTests
{
    [Fact]
    public async Task GivenStateAdvancesWhileSnapshotBuilds_WhenRefreshPublishes_ThenItDiscardsTheStaleGeneration()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        var refresher = new BlockingCacheRefresher();
        var publisher = new ConformanceRefreshPublisher(
            state,
            refresher,
            NullLogger<ConformanceRefreshPublisher>.Instance);

        var refresh = publisher.RefreshUntilCurrentAsync(CancellationToken.None);
        await refresher.FirstBuildStarted.Task;

        using (await state.AcquireActivationLockAsync(CancellationToken.None))
        {
            state.ApplyAndTrack(CreatePackageEvent(2, "second"));
        }
        refresher.ReleaseFirstBuild.TrySetResult();

        var publishedGeneration = await refresh;

        publishedGeneration.ShouldBe(2);
        refresher.BuiltGenerations.ShouldBe([1, 2]);
        refresher.PublishedGenerations.ShouldBe([2]);
    }

    [Fact]
    public async Task GivenCurrentGenerationAlreadyPublished_WhenRepositoryRefreshIsForced_ThenSameGenerationIsRebuiltAndPublished()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        var refresher = new BlockingCacheRefresher(blockFirstBuild: false);
        using var publisher = new ConformanceRefreshPublisher(
            state,
            refresher,
            NullLogger<ConformanceRefreshPublisher>.Instance);

        await publisher.RefreshUntilCurrentAsync(CancellationToken.None);
        await publisher.RefreshCurrentAsync(CancellationToken.None);

        refresher.BuiltGenerations.ShouldBe([1, 1]);
        refresher.PublishedGenerations.ShouldBe([1, 1]);
    }

    [Fact]
    public async Task GivenPublishedSnapshot_WhenRefreshCompletes_ThenItInvalidatesCachesAfterPublicationAndActivationLockRelease()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreatePackageEvent(1, "first"));
        var refresher = new BlockingCacheRefresher(state, blockFirstBuild: false);
        using var publisher = new ConformanceRefreshPublisher(
            state,
            refresher,
            NullLogger<ConformanceRefreshPublisher>.Instance);

        await publisher.RefreshUntilCurrentAsync(CancellationToken.None);

        refresher.Events.ShouldBe(["published", "invalidated"]);
    }

    private static SourceEvent CreatePackageEvent(long eventId, string packageId) =>
        new(
            eventId,
            $"package:{packageId}@1",
            nameof(PackageActivated),
            new PackageActivated(packageId, "1", []),
            DateTimeOffset.UtcNow);

    private sealed class BlockingCacheRefresher : IConformanceCacheRefresher
    {
        private readonly ConformanceState? _conformanceState;
        private readonly bool _blockFirstBuild;

        public BlockingCacheRefresher(
            ConformanceState? conformanceState = null,
            bool blockFirstBuild = true)
        {
            _conformanceState = conformanceState;
            _blockFirstBuild = blockFirstBuild;
        }

        public TaskCompletionSource FirstBuildStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstBuild { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<long> BuiltGenerations { get; } = [];
        public List<long> PublishedGenerations { get; } = [];
        public List<string> Events { get; } = [];

        public async Task<IConformanceConsumerSnapshot> BuildSnapshotAsync(
            ConformanceStateSnapshot stateSnapshot,
            long generation,
            CancellationToken cancellationToken)
        {
            BuiltGenerations.Add(generation);
            if (_blockFirstBuild && BuiltGenerations.Count == 1)
            {
                FirstBuildStarted.TrySetResult();
                await ReleaseFirstBuild.Task.WaitAsync(cancellationToken);
            }

            return new Snapshot(generation);
        }

        public void PublishSnapshot(IConformanceConsumerSnapshot snapshot)
        {
            PublishedGenerations.Add(snapshot.Generation);
            Events.Add("published");
        }

        public async ValueTask InvalidatePublishedSnapshotCachesAsync(
            IConformanceConsumerSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (_conformanceState is not null)
            {
                using (await _conformanceState.AcquireActivationLockAsync(cancellationToken))
                {
                }
            }

            Events.Add("invalidated");
        }

        private sealed record Snapshot(long Generation) : IConformanceConsumerSnapshot;
    }
}
