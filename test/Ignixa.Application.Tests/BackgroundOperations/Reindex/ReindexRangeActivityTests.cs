using System.Text.Json;
using DurableTask.Core;
using DurableTask.Core.Exceptions;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Workers;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexRangeActivityTests
{
    [Fact]
    public async Task GivenConcurrentRangeCompletions_WhenExecuted_ThenNoSingletonLockIsAcquired()
    {
        var fixture = new Fixture(concurrentReaders: 32);

        var outputs = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            fixture.Activity.RunAsync(null!, JsonSerializer.Serialize(
                new[] { new ReindexRangeInput("job", 1, "Patient", 1, 10, 42, 10, 0) }))));

        outputs.Length.ShouldBe(32);
        fixture.JobLock.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task GivenJobStoreFailsAfterTheWork_WhenRangeFinishes_ThenRangeStillReturnsItsCounts()
    {
        var fixture = new Fixture(failFinishingHeartbeat: true);

        var result = await fixture.Activity.RunAsync(null!, JsonSerializer.Serialize(
            new[] { new ReindexRangeInput("job", 1, "Patient", 1, 10, 42, 10, 0) }));

        JsonSerializer.Deserialize<ReindexRangeOutput>(result)!.ResourcesRead.ShouldBe(0);
        fixture.HeartbeatAttempts.ShouldBe(2);
    }

    [Fact]
    public async Task GivenDefinitionsBehindTarget_WhenRangeRuns_ThenItReturnsNotReadyWithoutReadingOrThrowing()
    {
        var fixture = new Fixture(definitionsEventId: 41);

        var result = await fixture.Activity.RunAsync(null!, JsonSerializer.Serialize(
            new[] { new ReindexRangeInput("job", 1, "Patient", 1, 10, 42, 10, 0) }));

        var output = JsonSerializer.Deserialize<ReindexRangeOutput>(result)!;
        output.IsDefinitionsNotReady.ShouldBeTrue();
        output.StaleDefinitionsEventId.ShouldBe(41);
        output.ResourcesRead.ShouldBe(0);
        fixture.RangeReads.ShouldBe(0);
    }

    [Fact]
    public async Task GivenJobStoreUnavailable_WhenRangeStarts_ThenTheActivityFailsBeforeReadingRanges()
    {
        var fixture = new Fixture(failEveryHeartbeat: true);

        await Should.ThrowAsync<TaskFailureException>(() => fixture.Activity.RunAsync(null!, JsonSerializer.Serialize(
            new[] { new ReindexRangeInput("job", 1, "Patient", 1, 10, 42, 10, 0) })));

        fixture.RangeReads.ShouldBe(0);
    }

    private sealed class Fixture
    {
        private int _heartbeatAttempts;
        private int _rangeReads;

        public Fixture(
            int concurrentReaders = 1,
            bool failFinishingHeartbeat = false,
            bool failEveryHeartbeat = false,
            long definitionsEventId = 42)
        {
            var tenants = Substitute.For<ITenantConfigurationStore>();
            tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
                .Returns(new TenantConfiguration { TenantId = 1, DisplayName = "Tenant", FhirVersion = "4.0" });
            var store = Substitute.For<IReindexStore>();
            var readers = 0;
            var readGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            store.ReadRangeAsync(
                    Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(),
                    Arg.Any<long?>(), Arg.Any<CancellationToken>())
                .Returns(async _ =>
                {
                    Interlocked.Increment(ref _rangeReads);
                    if (Interlocked.Increment(ref readers) == concurrentReaders)
                    {
                        readGate.TrySetResult();
                    }

                    await readGate.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    return (IReadOnlyList<ReindexResource>)Array.Empty<ReindexResource>();
                });
            var stores = Substitute.For<IReindexStoreFactory>();
            stores.GetReindexStoreAsync(1, Arg.Any<CancellationToken>()).Returns(store);
            var versions = Substitute.For<IFhirVersionContext>();
            versions.GetDefinitionsHandle(FhirVersion.R4, 1)
                .Returns(new DefinitionsHandle(
                    Substitute.For<ISearchIndexer>(), Substitute.For<IFhirSchemaProvider>(), definitionsEventId));
            var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
            repository.GetAsync("job", 1, Arg.Any<CancellationToken>()).Returns(_ => new BackgroundJob<ReindexJobDefinition>
            {
                JobId = "job", JobType = 4, Status = "Running", Definition = ReindexTestHelper.CreateJobDefinition()
            });
            repository.UpdateAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    var attempt = Interlocked.Increment(ref _heartbeatAttempts);
                    return failEveryHeartbeat || failFinishingHeartbeat && attempt > 1
                        ? Task.FromException(new TimeoutException("job store unavailable"))
                        : Task.CompletedTask;
                });

            var progress = new ReindexProgressReporter(
                repository, TimeProvider.System, NullLogger<ReindexProgressReporter>.Instance);
            Activity = new ReindexRangeActivity(
                new ReindexRangeProcessor(
                    stores,
                    tenants,
                    versions,
                    TestConformanceRefresher.Create(new ConformanceState()),
                    new FhirRequestContextAccessor()),
                progress);
        }

        public ReindexRangeActivity Activity { get; }

        public CountingJobLock JobLock { get; } = new();

        public int HeartbeatAttempts => _heartbeatAttempts;

        public int RangeReads => _rangeReads;
    }

    private sealed class CountingJobLock : IReindexJobLock
    {
        private int _calls;
        public int Calls => _calls;

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return action(cancellationToken);
        }
    }
}
