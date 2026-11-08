using Ignixa.Application.Features.Conformance;
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

    // §9: a candidate made obsolete before the commit, or by the winner of a lost append, commits nothing.
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task GivenACandidateMadeObsolete_WhenCommitted_ThenItStopsWithoutAppendingOrRequestingReindex(
        bool byAnotherWriter,
        int expectedAppendAttempts)
    {
        using var harness = new Harness();
        var hideEventId = harness.SeedShadow();
        if (byAnotherWriter)
        {
            harness.Log.OtherWriters.Enqueue(Committed(1, hideEventId));
        }
        else
        {
            harness.State.FindByCanonical(BaseIdentifier)!.DeactivationEventId = hideEventId + 1;
        }

        harness.State.GetTransitionCandidates(hideEventId).Count.ShouldBe(1);

        var committed = await harness.Committer().CommitAsync(hideEventId, CancellationToken.None);

        (committed, harness.Log.AppendAttempts).ShouldBe((false, expectedAppendAttempts));
        harness.Log.Appends.ShouldBeEmpty();
        await harness.Trigger.DidNotReceiveWithAnyArgs().RequestReindexAsync(default!, default);
    }

    // Review 5 N6: one package activation is one phase-two transition, however many codes it hides.
    [Fact]
    public async Task GivenSeventyShadowsInOneActivation_WhenTheBatchIsCommitted_ThenOneAppendOneReindexRequestAndOneRefreshCommitThemAll()
    {
        using var harness = new Harness();
        harness.SeedPackage("seed", shadowsSeed: false);
        var batchId = harness.SeedPackage("shadow", shadowsSeed: true);

        var committed = await harness.Committer().CommitAsync(batchId, CancellationToken.None);

        committed.ShouldBeTrue();
        harness.Log.Appends.ShouldHaveSingleItem().Count.ShouldBe(ShadowCount);
        Enumerable.Range(1, ShadowCount).Select(index => harness.Owner(Code(index)))
            .ShouldBe(Enumerable.Range(1, ShadowCount).Select(index => (Canonical($"shadow-{index}"), SearchParameterStatus.Pending)));
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
        harness.Owner(Code(1)).ShouldBe((Canonical("second-1"), SearchParameterStatus.Pending));
        harness.Owner(Code(2)).ShouldBe((Canonical("first-2"), SearchParameterStatus.Pending));
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

        public (string Canonical, SearchParameterStatus Status) Owner(string code) =>
            State.GetSearchParameter("Patient", code) is { } owner ? (owner.Canonical, owner.Status) : default;

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
                Seed(Activated(canonicals[index - 1], Code(index), index, shadowsSeed ? Canonical($"seed-{index}") : null, $"{package}@1.0.0", expression: "Patient.identifier"));
            }

            return Seed(PackageActivated(package, "1.0.0", canonicals));
        }

        public void Dispose() => State.Dispose();
    }
}
