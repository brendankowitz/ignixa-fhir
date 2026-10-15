using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Search.Exceptions;
using Shouldly;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Tests.Features.Conformance;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Models;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;
using static Ignixa.Application.Tests.Features.Search.TestDefinitions;
using SearchParamInfo = Ignixa.Search.Models.SearchParameterInfo;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Search;

public class PackageSearchParameterInheritanceTests
{
    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenPackageParameter_WhenLoadedEagerlyOrLazily_ThenInheritedParametersRemainAvailable(FhirVersion version)
    {
        var baseManager = BaseManager(version);
        using var state = await InitializedStateAsync(PackageCustom());

        foreach (bool eager in new[] { true, false })
        {
            var manager = await CreateManagerAsync(state, eager, version);

            foreach (string resourceType in new[] { "Patient", "Observation", "Organization", "Bundle" })
            {
                foreach (string code in new[] { "_id", "_lastUpdated", "_tag" })
                {
                    manager.TryGetSearchParameter(resourceType, code, out var parameter)
                        .ShouldBeTrue($"{version}, eager={eager}: {resourceType}.{code}");
                    parameter.Url.ShouldBe(baseManager.GetSearchParameter(resourceType, code).Url);
                }
            }

            manager.GetSearchParameter("Patient", "package-custom").Expression.ShouldBe("Patient.active");
            manager.ClearCache();
            manager.GetSearchParameter("Patient", "_id").ShouldNotBeNull();
            manager.ReloadFromConformanceState();
            manager.GetSearchParameter("Patient", "_id").ShouldNotBeNull();
            manager.GetSearchParameter("Patient", "package-custom").ShouldNotBeNull();
        }
    }

    [Theory]
    [InlineData("ViewDefinition", true)]
    [InlineData("ViewDefinition", false)]
    [InlineData("CustomPackageResource", true)]
    [InlineData("CustomPackageResource", false)]
    public async Task GivenPackageResource_WhenLoadedEagerlyOrLazily_ThenOwnAndUniversalParametersSurviveCacheReload(
        string resourceType, bool eager)
    {
        var baseManager = BaseManager();
        using var state = await InitializedStateAsync(PackageCustom(resourceType, $"{resourceType}.id"));
        var manager = await CreateManagerAsync(state, eager);

        AssertParameters();
        manager.ClearCache();
        AssertParameters();
        manager.ReloadFromConformanceState();
        AssertParameters();

        void AssertParameters()
        {
            manager.GetSearchParameter(resourceType, "package-custom").Expression.ShouldBe($"{resourceType}.id");
            foreach (string code in new[] { "_id", "_lastUpdated", "_tag" })
            {
                manager.TryGetSearchParameter(resourceType, code, out var parameter)
                    .ShouldBeTrue($"{resourceType}.{code}, eager={eager}");
                parameter.Url.ShouldBe(baseManager.GetSearchParameter("Resource", code).Url);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenUnknownTypeWithoutPackageParameters_WhenRequested_ThenTypeRemainsUnsupported(bool eager)
    {
        using var state = await InitializedStateAsync(PackageCustom());
        var manager = await CreateManagerAsync(state, eager);

        Should.Throw<SearchResourceNotSupportedException>(
            () => manager.GetSearchParameters("UnregisteredResource").ToList());
    }

    private const string PatientName = "http://hl7.org/fhir/SearchParameter/Patient-name";
    private static readonly string StagedName = TestPackages.Canonical("staged");

    public enum PendingScenario
    {
        CustomParameter,
        DistinctIdentityOverride,
        RestoredBase,
    }

    // §4.2: a Pending parameter is extracted but hidden from search unless the request opts in to partial indices.
    [Theory]
    [InlineData(PendingScenario.CustomParameter, "package-custom", "http://example.org/SearchParameter/package-custom")]
    [InlineData(PendingScenario.DistinctIdentityOverride, "identifier", "http://example.org/SearchParameter/distinct-identifier")]
    [InlineData(PendingScenario.RestoredBase, "identifier", TestPackages.BaseIdentifier)]
    public async Task GivenAPendingParameter_WhenSearchDefinitionsAreResolved_ThenItIsExtractedButHiddenUnlessPartialIndicesAreRequested(
        PendingScenario scenario,
        string code,
        string extractedUrl)
    {
        object[] events = scenario switch
        {
            PendingScenario.CustomParameter => [PackageCustom()],
            PendingScenario.DistinctIdentityOverride => [Activated(TestPackages.Canonical("distinct-identifier"), "identifier", 99)],
            _ =>
            [
                Activated(TestPackages.BaseIdentifier, "identifier", 1, package: CorePackage),
                Activated(TestPackages.Canonical("identifier"), "identifier", 1, TestPackages.BaseIdentifier),
                Committed(1, 2),
                PackageDeactivated("custom.package"),
                Committed(1, 4),
            ],
        };
        using var state = await InitializedStateAsync(events);
        using var context = CreateContext().Publish(state);

        context.GetSearchParameterDefinitionManager(FhirVersion.R4, 1).GetSearchParameter("Patient", code).Url.ShouldBe(new Uri(extractedUrl));
        context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1).TryGetSearchParameter("Patient", code, out _).ShouldBeFalse();
        context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1, () => true)
            .TryGetSearchParameter("Patient", code, out var partial).ShouldBeTrue();
        (partial.IsSearchable, partial.IsSupported).ShouldBe((false, true));
    }

    // §4.2, §4.4: a shared-identity override in transition stays hidden even with the partial-index header.
    [Fact]
    public async Task GivenASharedIdentityOverrideInTransition_WhenSearchDefinitionsAreResolved_ThenTheCodeIsNotVisibleEvenWithPartialIndices()
    {
        using var state = await InitializedStateAsync(LifecycleEvents());
        using var context = CreateContext().Publish(state);

        var partialDefinitions = context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1, () => true);

        partialDefinitions.TryGetSearchParameter("Patient", "name", out _).ShouldBeFalse();
        partialDefinitions.TryGetSearchParameter("Patient", "disabling", out _).ShouldBeFalse();
    }

    // B-L2: on a web farm one instance writes while another, up to a grace window behind, searches. Each prefix of the
    // real activation/commit/reindex stream is one instance's view, and every ordered pair is one lag.
    [Fact]
    public async Task GivenLaggingInstancesAtEachShadowTransitionCut_WhenDefinitionsReplay_ThenNoPairBindsDifferentStorageIdentity()
    {
        var overrideCanonical = TestPackages.Canonical("shadow.package");
        var events = await RecordShadowLifecycleAsync(overrideCanonical);
        events.Select(row => row.Data.GetType().Name).ShouldBe([nameof(SearchParameterActivated), nameof(SearchParameterActivated),
            nameof(PackageActivated), nameof(SearchParameterTransitionCommitted), nameof(SearchParameterReindexStarted), nameof(SearchParameterReindexCompleted)]);

        var instances = new List<(int Cut, SearchParamInfo Extraction, SearchParamInfo? Searchable)>();
        for (var cut = 0; cut <= events.Count; cut++)
        {
            using var state = await InitializedStateAsync([.. events.Take(cut).Select(row => row.Data)]);
            using var context = CreateContext().Publish(state);
            var extraction = context.GetSearchParameterDefinitionManager(FhirVersion.R4, 1).GetSearchParameter("Patient", "identifier");
            var searchable = context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1)
                .TryGetSearchParameter("Patient", "identifier", out var visible) ? visible : null;
            instances.Add((cut, extraction, searchable));
        }

        // Not vacuous: the code is visible before the hide and after the reindex, and hidden in between.
        instances.Where(instance => instance.Searchable is not null).Select(instance => instance.Cut).ShouldBe([0, 1, events.Count]);
        instances[^1].Searchable!.Url.ShouldBe(new Uri(overrideCanonical));
        foreach (var writer in instances)
        {
            foreach (var reader in instances.Where(instance => instance.Searchable is not null))
            {
                Identity(reader.Searchable!).ShouldBe(
                    Identity(writer.Extraction),
                    $"instance at cut {reader.Cut} searches an identity instance at cut {writer.Cut} does not extract");
                Identity(writer.Extraction).ShouldBe(new Uri(TestPackages.BaseIdentifier));
            }
        }

        static Uri Identity(SearchParamInfo parameter) => parameter.OverridesUrl ?? parameter.Url;
    }

    // Shadows Patient.identifier, commits and reindexes it through the production writers; returns the durable stream.
    private static async Task<IReadOnlyList<SourceEvent>> RecordShadowLifecycleAsync(string overrideCanonical)
    {
        var log = new InMemoryEventLog();
        using var state = new ConformanceState();
        await state.InitializeFromEventsAsync(log, CancellationToken.None);
        var pipeline = TestPackageActivationPipeline.Create(TestPackages.Repository(TestPackages.Shadow("shadow.package")), log, state);
        (await pipeline.ActivateAsync("shadow.package", "1.0.0", CancellationToken.None)).Success.ShouldBeTrue();

        var transitionId = log.Events.Single(row => row.Data is PackageActivated).EventId;
        var committer = new SearchParameterTransitionCommitter(log, state, TestPackageActivationPipeline.NoJobTrigger(), TestConformanceRefresher.Create(state, log));
        (await committer.CommitAsync(transitionId, CancellationToken.None)).ShouldBeTrue();

        var pending = state.FindByCanonical(overrideCanonical)!;
        pending.Status.ShouldBe(SearchParameterStatus.Pending);
        var target = new ReindexParameterDefinition(
            pending.Canonical, pending.Code, pending.ResourceType, pending.SearchParamId, pending.ActivationEventId, ["Patient"], pending.OverridesCanonical, pending.FhirVersion);
        var lifecycle = new ReindexLifecycleEventWriter(log, state);
        (await lifecycle.StartAsync("job", [target], CancellationToken.None)).ShouldBeEmpty();
        (await lifecycle.CompleteAsync("job", [new ReindexTargetCompletion(target, true, 1, null)], TimeSpan.Zero, CancellationToken.None)).ShouldBeEmpty();
        state.FindByCanonical(overrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Enabled);
        return log.Events.ToArray();
    }

    // Patient.reindexing is Reindexing, Patient.disabling is Disabling, and an override of Patient.name is Staged.
    private static object[] LifecycleEvents() =>
    [
        Activated(TestPackages.Canonical("reindexing"), "reindexing", 1),
        ReindexStarted(TestPackages.Canonical("reindexing"), "reindexing", 1, "job"),
        Activated(TestPackages.Canonical("disabling"), "disabling", 2, expression: "Patient.active"),
        Deactivated(TestPackages.Canonical("disabling"), "disabling"),
        Activated(PatientName, "name", 3, package: CorePackage, type: SearchParamType.String),
        Activated(StagedName, "name", 3, PatientName, type: SearchParamType.String),
    ];

    private static SearchParameterActivated PackageCustom(string resourceType = "Patient", string? expression = null) =>
        Activated(TestPackages.Canonical("package-custom"), "package-custom", 1, resourceType: resourceType, expression: expression ?? $"{resourceType}.active");
}
