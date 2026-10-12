using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;
using static Ignixa.Application.Tests.Features.Conformance.TestPackages;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>Spec §4.4 phase two, and §9 concurrent writers of the conformance stream.</summary>
public class SearchParameterTransitionCommitterTests
{
    private const int ShadowCount = 70;
    private static readonly string OverrideCanonical = Canonical("Patient-identifier");

    [Fact]
    public async Task GivenMatchingStagedTransition_WhenCommitted_ThenItPromotesTheParameterAndRequestsReindex()
    {
        using var harness = new Harness();
        var hideEventId = harness.SeedShadow();

        var committed = await harness.Committer().CommitAsync(hideEventId, CancellationToken.None);

        committed.ShouldBeTrue();
        var owner = harness.State.GetSearchParameter("Patient", "identifier")!;
        owner.Canonical.ShouldBe(OverrideCanonical);
        owner.Status.ShouldBe(SearchParameterStatus.Pending);
        await harness.Trigger.Received(1).RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAlreadyCommittedTransition_WhenCommittedAgain_ThenItCompletesWithoutAppendingOrTriggering()
    {
        using var harness = new Harness();
        var hideEventId = harness.SeedShadow();
        harness.Seed(Committed(1, hideEventId));

        var committed = await harness.Committer().CommitAsync(hideEventId, CancellationToken.None);

        committed.ShouldBeFalse();
        harness.Log.AppendAttempts.ShouldBe(0);
        await harness.Trigger.DidNotReceiveWithAnyArgs().RequestReindexAsync(default!, default);
    }

    [Fact]
    public async Task GivenAStaleCandidateThatWouldBeIgnored_WhenCommitted_ThenItDoesNotAppendANoOpEvent()
    {
        using var harness = new Harness();
        var hideEventId = harness.SeedShadow();
        harness.State.FindByCanonical(BaseIdentifier)!.DeactivationEventId = hideEventId + 1;
        harness.State.GetTransitionCandidates(hideEventId).Count.ShouldBe(1);

        var committed = await harness.Committer().CommitAsync(hideEventId, CancellationToken.None);

        committed.ShouldBeFalse();
        harness.Log.AppendAttempts.ShouldBe(0);
    }

    // §9: a lost append catches up and rebuilds the transition; here the winner already committed it.
    [Fact]
    public async Task GivenNewerTransitionMakesCandidateObsolete_WhenCommitted_ThenItStopsWithoutRetryingAppend()
    {
        using var harness = new Harness();
        var hideEventId = harness.SeedShadow();
        harness.Log.OtherWriters.Enqueue(Committed(1, hideEventId));

        var committed = await harness.Committer().CommitAsync(hideEventId, CancellationToken.None);

        committed.ShouldBeFalse();
        harness.Log.AppendAttempts.ShouldBe(1);
        harness.Log.Appends.ShouldBeEmpty();
        await harness.Trigger.DidNotReceiveWithAnyArgs().RequestReindexAsync(default!, default);
    }

    [Fact]
    public async Task GivenPersistentConcurrencyConflicts_WhenCommitted_ThenItThrowsAfterBoundedRetries()
    {
        using var harness = new Harness();
        var hideEventId = harness.SeedShadow();
        foreach (var index in Enumerable.Range(1, 5))
        {
            harness.Log.OtherWriters.Enqueue(PackageActivated($"unrelated-{index}"));
        }

        await Should.ThrowAsync<SourceEventConcurrencyException>(() =>
            harness.Committer().CommitAsync(hideEventId, CancellationToken.None));

        harness.Log.AppendAttempts.ShouldBe(3);
    }

    // N6: one package activation is one phase-two transition, however many codes it hides.
    [Fact]
    public async Task GivenSeventyShadowsInOneActivation_WhenTheBatchIsCommitted_ThenOneAppendOneReindexRequestAndOneRefreshCommitThemAll()
    {
        using var harness = new Harness();
        harness.SeedPackage("seed", shadowsSeed: false);
        var batchId = harness.SeedPackage("shadow", shadowsSeed: true);

        var committed = await harness.Committer().CommitAsync(batchId, CancellationToken.None);

        committed.ShouldBeTrue();
        harness.Log.Appends.ShouldHaveSingleItem().Count.ShouldBe(ShadowCount);
        Enumerable.Range(1, ShadowCount).ShouldAllBe(index =>
            harness.State.GetSearchParameter("Patient", Code(index))!.Canonical == Canonical($"shadow-{index}") &&
            harness.State.GetSearchParameter("Patient", Code(index))!.Status == SearchParameterStatus.Pending);
        await harness.Trigger.Received(1).RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await harness.Tenants.Received(1).GetAllTenantsAsync(Arg.Any<CancellationToken>());
        harness.State.GetTransitionHideEventIds().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenALaterActivationShadowsACodeOfAnUncommittedBatch_WhenBothBatchesCommit_ThenEachCommitsOnlyTheCodesItStillHides(
        bool laterBatchCommitsFirst)
    {
        using var harness = new Harness();
        harness.SeedPackage("seed", shadowsSeed: false, count: 2);
        var firstBatch = harness.SeedPackage("first", shadowsSeed: true, count: 2);
        var secondBatch = harness.SeedPackage("second", shadowsSeed: true, count: 1);
        var committer = harness.Committer();

        harness.State.GetTransitionParameters(firstBatch).Select(parameter => parameter.Code).Distinct().ShouldBe([Code(2)]);
        harness.State.GetTransitionParameters(secondBatch).Select(parameter => parameter.Code).Distinct().ShouldBe([Code(1)]);
        foreach (var batch in laterBatchCommitsFirst ? new[] { secondBatch, firstBatch } : [firstBatch, secondBatch])
        {
            (await committer.CommitAsync(batch, CancellationToken.None)).ShouldBeTrue();
        }

        harness.Log.Appends.Count.ShouldBe(2);
        harness.Log.Appends.ShouldAllBe(append => append.Count == 1);
        harness.State.GetSearchParameter("Patient", Code(1))!.Canonical.ShouldBe(Canonical("second-1"));
        harness.State.GetSearchParameter("Patient", Code(2))!.Canonical.ShouldBe(Canonical("first-2"));
        harness.State.GetSearchParameter("Patient", Code(1))!.Status.ShouldBe(SearchParameterStatus.Pending);
        harness.State.GetSearchParameter("Patient", Code(2))!.Status.ShouldBe(SearchParameterStatus.Pending);
        harness.State.FindByCanonical(Canonical("first-1"))!.Status.ShouldBe(SearchParameterStatus.Disabled);
        harness.State.GetTransitionHideEventIds().ShouldBeEmpty();
    }

    private static string Code(int index) => $"code-{index}";

    private sealed class Harness : IDisposable
    {
        public InMemoryEventLog Log { get; } = new();

        public ConformanceState State { get; } = new();

        public IReindexTrigger Trigger { get; } = Substitute.For<IReindexTrigger>();

        public ITenantConfigurationStore Tenants { get; } = TestConformanceRefresher.Tenants();

        public SearchParameterTransitionCommitter Committer() =>
            new(Log, State, Trigger, TestConformanceRefresher.Create(State, tenants: Tenants));

        public long Seed(object data) => Log.Seed(State, data);

        /// <summary>Seeds the base Patient.identifier and an override staged against it; returns its hide event id.</summary>
        public long SeedShadow()
        {
            Seed(Activated(BaseIdentifier, "identifier", 1, package: CorePackage));
            return Seed(Activated(OverrideCanonical, "identifier", 1, BaseIdentifier));
        }

        /// <summary>Seeds one package activation of <paramref name="count"/> codes; returns its PackageActivated id.</summary>
        public long SeedPackage(string package, bool shadowsSeed, int count = ShadowCount)
        {
            var canonicals = Enumerable.Range(1, count).Select(index => Canonical($"{package}-{index}")).ToArray();
            for (var index = 1; index <= count; index++)
            {
                Seed(Activated(
                    canonicals[index - 1],
                    Code(index),
                    index,
                    shadowsSeed ? Canonical($"seed-{index}") : null,
                    $"{package}@1.0.0",
                    expression: "Patient.identifier"));
            }

            return Seed(PackageActivated(package, "1.0.0", canonicals));
        }

        public void Dispose() => State.Dispose();
    }
}
