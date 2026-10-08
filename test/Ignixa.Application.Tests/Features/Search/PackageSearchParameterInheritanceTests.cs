using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
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
        var baseManager = new SearchParameterDefinitionManager(
            version.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);

        foreach (bool eager in new[] { true, false })
        {
            var manager = new CompositeSearchParameterDefinitionManager(
                baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
                new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = eager });
            await manager.InitializeAsync();

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

    [Fact]
    public async Task GivenPackageOnlyResourceType_WhenLoadedEagerly_ThenItsOwnParameterRemainsAvailable()
    {
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events("CustomPackageResource"));
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions
            {
                EagerLoadPackageSearchParameters = true,
                FailStartupOnEagerLoadError = true,
            });

        await manager.InitializeAsync();

        manager.GetSearchParameter("CustomPackageResource", "package-custom").Expression.ShouldBe("CustomPackageResource.active");
    }

    [Theory]
    [InlineData("ViewDefinition", true)]
    [InlineData("ViewDefinition", false)]
    [InlineData("CustomPackageResource", true)]
    [InlineData("CustomPackageResource", false)]
    public async Task GivenPackageResource_WhenLoadedEagerlyOrLazily_ThenOwnAndUniversalParametersSurviveCacheReload(
        string resourceType, bool eager)
    {
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events(resourceType, $"{resourceType}.id"));
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions
            {
                EagerLoadPackageSearchParameters = eager,
                FailStartupOnEagerLoadError = true,
            });

        await manager.InitializeAsync();
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
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = eager });
        await manager.InitializeAsync();

        Should.Throw<SearchResourceNotSupportedException>(
            () => manager.GetSearchParameters("UnregisteredResource").ToList());
    }

    [Fact]
    public async Task GivenAPendingParameter_WhenDefinitionsResolveSearchVisibility_ThenItIsNotSearchableByDefault()
    {
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true });

        await manager.InitializeAsync();

        var parameter = manager.GetSearchParameter("Patient", "package-custom");

        parameter.IsSearchable.ShouldBeFalse();
        parameter.IsSupported.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAPendingParameter_WhenSearchDefinitionsAreResolved_ThenItIsHiddenUnlessPartialIndicesAreRequested()
    {
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        using var context = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true },
            NullFhirBaseUriProvider.Instance,
            conformanceState: state);

        var defaultDefinitions = context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1);
        var partialDefinitions = context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1, () => true);

        defaultDefinitions.TryGetSearchParameter("Patient", "package-custom", out _).ShouldBeFalse();
        partialDefinitions.TryGetSearchParameter("Patient", "package-custom", out var parameter).ShouldBeTrue();
        parameter.IsSearchable.ShouldBeFalse();
        parameter.IsSupported.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenASharedIdentityOverrideInTransition_WhenSearchDefinitionsAreResolved_ThenTheCodeIsNotVisibleEvenWithPartialIndices()
    {
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(LifecycleEvents());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        using var context = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true },
            NullFhirBaseUriProvider.Instance,
            conformanceState: state);

        var partialDefinitions = context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1, () => true);

        partialDefinitions.TryGetSearchParameter("Patient", "name", out _).ShouldBeFalse();
        partialDefinitions.TryGetSearchParameter("Patient", "disabling", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenADistinctIdentityPendingOverride_WhenSearchDefinitionsAreResolved_ThenTheCodeIsHiddenAndNotBoundToBase()
    {
        const string baseUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(DistinctIdentityOverrideEvents());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        using var context = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true },
            NullFhirBaseUriProvider.Instance,
            conformanceState: state);

        var extractionDefinitions = context.GetSearchParameterDefinitionManager(FhirVersion.R4, 1);
        var searchDefinitions = context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1);

        extractionDefinitions.GetSearchParameter("Patient", "identifier").Url.ShouldNotBe(new Uri(baseUrl));
        searchDefinitions.TryGetSearchParameter("Patient", "identifier", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenRestoredBaseIsPending_WhenSearchDefinitionsAreResolved_ThenItStaysHiddenUntilEnabled()
    {
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(RestoredBasePendingEvents());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        using var context = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true },
            NullFhirBaseUriProvider.Instance,
            conformanceState: state);

        var extractionDefinitions = context.GetSearchParameterDefinitionManager(FhirVersion.R4, 1);
        var searchDefinitions = context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1);

        extractionDefinitions.GetSearchParameter("Patient", "identifier").Url.ShouldBe(
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        searchDefinitions.TryGetSearchParameter("Patient", "identifier", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenLifecycleStatuses_WhenDefinitionsAreLoaded_ThenExtractionIncludesTransitionalOwnersButNotStagedOverrides()
    {
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(LifecycleEvents());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true });

        await manager.InitializeAsync();

        manager.TryGetSearchParameter("Patient", "reindexing", out _).ShouldBeTrue();
        manager.TryGetSearchParameter("Patient", "disabling", out _).ShouldBeTrue();
        manager.TryGetSearchParameter(new Uri("http://example.org/SearchParameter/staged"), out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenLifecycleStatuses_WhenAResourceIsIndexed_ThenDisablingIsExtractedButStagedIsNot()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var baseManager = new SearchParameterDefinitionManager(
            schema, NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(LifecycleEvents());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true });
        await manager.InitializeAsync();
        var indexer = SearchIndexerFactory.CreateInstance(
            schema,
            NullLoggerFactory.Instance,
            manager,
            NullFhirBaseUriProvider.Instance);
        var patient = ResourceJsonNode.Parse(
            """
            {
              "resourceType": "Patient",
              "active": true,
              "name": [{ "family": "Hidden" }]
            }
            """).ToElement(schema);

        var entries = indexer.Extract(patient);

        entries.ShouldContain(entry => entry.SearchParameter.Code == "disabling");
        entries.ShouldNotContain(entry => entry.SearchParameter.Url == new Uri("http://example.org/SearchParameter/staged"));
    }

    [Fact]
    public async Task GivenShadowedCodeBeforeAndAfterCommit_WhenAResourceIsIndexed_ThenStorageIdentityStaysOnTheBase()
    {
        const string baseUrl = "http://hl7.org/fhir/SearchParameter/Patient-name";
        const string overrideUrl = "http://example.org/SearchParameter/staged";
        var schema = FhirVersion.R4.GetSchemaProvider();
        var patient = ResourceJsonNode.Parse(
            """
            {
              "resourceType": "Patient",
              "name": [{ "family": "Shadowed" }]
            }
            """).ToElement(schema);

        var stagedEntries = await ExtractAsync(LifecycleEvents(), patient);
        var staged = stagedEntries.Single(entry => entry.SearchParameter.Url == new Uri(baseUrl));
        staged.SearchParameter.Url.ShouldBe(new Uri(baseUrl));

        var committedEntries = await ExtractAsync(CommittedOverrideEvents(), patient);
        var committed = committedEntries.Single(entry => entry.SearchParameter.Url == new Uri(overrideUrl));
        committed.SearchParameter.Url.ShouldBe(new Uri(overrideUrl));
        committed.SearchParameter.OverridesUrl.ShouldBe(new Uri(baseUrl));

        async Task<IReadOnlyCollection<SearchIndexEntry>> ExtractAsync(
            IAsyncEnumerable<SourceEvent> events,
            IElement resource)
        {
            var baseManager = new SearchParameterDefinitionManager(
                schema, NullLogger<SearchParameterDefinitionManager>.Instance);
            using var state = new ConformanceState();
            var store = Substitute.For<ISourceEventStore>();
            store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(events);
            await state.InitializeFromEventsAsync(store, CancellationToken.None);
            var manager = new CompositeSearchParameterDefinitionManager(
                baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
                new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true });
            await manager.InitializeAsync();
            return SearchIndexerFactory.CreateInstance(
                    schema,
                    NullLoggerFactory.Instance,
                    manager,
                    NullFhirBaseUriProvider.Instance)
                .Extract(resource);
        }
    }

    [Fact]
    public async Task GivenLaggingInstancesAtEachShadowTransitionCut_WhenDefinitionsReplay_ThenNoPairBindsDifferentStorageIdentity()
    {
        var events = new List<SourceEvent>();
        await foreach (var row in SharedIdentityTransitionEvents())
        {
            events.Add(row);
        }

        foreach (int eventCount in new[] { 1, 2, 3 })
        {
            using var state = new ConformanceState();
            var store = Substitute.For<ISourceEventStore>();
            store.ReadAllAsync(Arg.Any<CancellationToken>())
                .Returns(ReplayCut(events, eventCount));
            await state.InitializeFromEventsAsync(store, CancellationToken.None);
            using var context = new FhirVersionContext(
                NullLoggerFactory.Instance,
                new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true },
                NullFhirBaseUriProvider.Instance,
                conformanceState: state);
            var extraction = context.GetSearchParameterDefinitionManager(FhirVersion.R4, 1);
            var search = context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, 1);

            if (search.TryGetSearchParameter("Patient", "identifier", out var searchable))
            {
                var extracted = extraction.GetSearchParameter("Patient", "identifier");
                (extracted.OverridesUrl ?? extracted.Url).ShouldBe(searchable.OverridesUrl ?? searchable.Url);
            }
        }

        static async IAsyncEnumerable<SourceEvent> ReplayCut(
            IReadOnlyList<SourceEvent> rows,
            int eventCount)
        {
            await Task.CompletedTask;
            for (var index = 0; index < eventCount; index++)
            {
                yield return rows[index];
            }
        }
    }

    [Fact]
    public async Task GivenSameCanonicalUpgradeIsStaged_WhenResolvedByCanonical_ThenOutgoingDefinitionRemainsExtracted()
    {
        const string canonical = "http://example.org/SearchParameter/same-canonical";
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(SameCanonicalUpgradeEvents(canonical));
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = false });
        await manager.InitializeAsync();

        manager.TryGetSearchParameter(new Uri(canonical), out var parameter).ShouldBeTrue();
        parameter.Expression.ShouldBe("Patient.name");
    }

    private static async IAsyncEnumerable<SourceEvent> Events(string resourceType = "Patient", string? expression = null)
    {
        await Task.CompletedTask;
        yield return new SourceEvent(
            1, "package-inheritance", nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/package-custom", "package-custom", resourceType,
                expression ?? $"{resourceType}.active", SearchParamType.Token, "inheritance.package@1.0", null, 1, null, null, null, null),
            DateTimeOffset.UtcNow);
    }

    private static async IAsyncEnumerable<SourceEvent> LifecycleEvents()
    {
        await Task.CompletedTask;
        yield return Activation(1, "http://example.org/SearchParameter/reindexing", "reindexing", 1);
        yield return new SourceEvent(
            2,
            "package-lifecycle",
            nameof(SearchParameterReindexStarted),
            new SearchParameterReindexStarted(
                "http://example.org/SearchParameter/reindexing",
                "reindexing",
                "Patient",
                "job",
                ["Patient"],
                1),
            DateTimeOffset.UtcNow);
        yield return Activation(
            3,
            "http://example.org/SearchParameter/disabling",
            "disabling",
            2,
            expression: "Patient.active");
        yield return new SourceEvent(
            4,
            "package-lifecycle",
            nameof(SearchParameterDeactivated),
            new SearchParameterDeactivated(
                "http://example.org/SearchParameter/disabling",
                "disabling",
                "Patient",
                "test"),
            DateTimeOffset.UtcNow);
        yield return new SourceEvent(
            5,
            "package-lifecycle",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://hl7.org/fhir/SearchParameter/Patient-name",
                "name",
                "Patient",
                "Patient.name",
                SearchParamType.String,
                "hl7.fhir.r4.core@4.0.1",
                null,
                3,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);
        yield return new SourceEvent(
            6,
            "package-lifecycle",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/staged",
                "name",
                "Patient",
                "Patient.name",
                SearchParamType.String,
                "custom.package@1.0",
                new OverrideInfo("http://hl7.org/fhir/SearchParameter/Patient-name", 3),
                3,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);
    }

    private static async IAsyncEnumerable<SourceEvent> DistinctIdentityOverrideEvents()
    {
        await Task.CompletedTask;
        yield return new SourceEvent(
            1,
            "package-distinct-override",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/distinct-identifier",
                "identifier",
                "Patient",
                "Patient.identifier",
                SearchParamType.Token,
                "custom.package@1.0",
                null,
                99,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);
    }

    private static async IAsyncEnumerable<SourceEvent> CommittedOverrideEvents()
    {
        await foreach (var row in LifecycleEvents())
        {
            yield return row;
        }

        yield return new SourceEvent(
            7,
            "package-lifecycle",
            nameof(SearchParameterTransitionCommitted),
            new SearchParameterTransitionCommitted(3, [6], [6]),
            DateTimeOffset.UtcNow);
    }

    private static async IAsyncEnumerable<SourceEvent> RestoredBasePendingEvents()
    {
        await Task.CompletedTask;
        yield return Activation(
            1,
            "http://hl7.org/fhir/SearchParameter/Patient-identifier",
            "identifier",
            1,
            "hl7.fhir.r4.core@4.0.1");
        yield return new SourceEvent(
            2,
            "package-restore",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/identifier",
                "identifier",
                "Patient",
                "Patient.identifier",
                SearchParamType.Token,
                "custom.package@1.0",
                new OverrideInfo("http://hl7.org/fhir/SearchParameter/Patient-identifier", 1),
                1,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);
        yield return new SourceEvent(
            3,
            "package-restore",
            nameof(SearchParameterTransitionCommitted),
            new SearchParameterTransitionCommitted(1, [2], [2]),
            DateTimeOffset.UtcNow);
        yield return new SourceEvent(
            4,
            "package-restore",
            nameof(PackageDeactivated),
            new PackageDeactivated("custom.package", "1.0", "test"),
            DateTimeOffset.UtcNow);
        yield return new SourceEvent(
            5,
            "package-restore",
            nameof(SearchParameterTransitionCommitted),
            new SearchParameterTransitionCommitted(1, [4], [4]),
            DateTimeOffset.UtcNow);
    }

    private static async IAsyncEnumerable<SourceEvent> SharedIdentityTransitionEvents()
    {
        await Task.CompletedTask;
        yield return Activation(
            1,
            "http://hl7.org/fhir/SearchParameter/Patient-identifier",
            "identifier",
            1,
            "hl7.fhir.r4.core@4.0.1");
        yield return new SourceEvent(
            2,
            "package-shared-identity",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/identifier",
                "identifier",
                "Patient",
                "Patient.identifier",
                SearchParamType.Token,
                "custom.package@1.0",
                new OverrideInfo("http://hl7.org/fhir/SearchParameter/Patient-identifier", 1),
                1,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);
        yield return new SourceEvent(
            3,
            "package-shared-identity",
            nameof(SearchParameterTransitionCommitted),
            new SearchParameterTransitionCommitted(1, [2], [2]),
            DateTimeOffset.UtcNow);
    }

    private static SourceEvent Activation(
        long eventId,
        string canonical,
        string code,
        int searchParamId,
        string sourcePackage = "custom.package@1.0",
        string? expression = null) =>
        new(
            eventId,
            "package-lifecycle",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                canonical,
                code,
                "Patient",
                expression ?? $"Patient.{code}",
                SearchParamType.Token,
                sourcePackage,
                null,
                searchParamId,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);

    private static async IAsyncEnumerable<SourceEvent> SameCanonicalUpgradeEvents(string canonical)
    {
        await Task.CompletedTask;
        yield return new SourceEvent(
            1,
            "package-same-canonical",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                canonical,
                "same-canonical",
                "Patient",
                "Patient.name",
                SearchParamType.String,
                "custom.package@1.0",
                null,
                1,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);
        yield return new SourceEvent(
            2,
            "package-same-canonical",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                canonical,
                "same-canonical",
                "Patient",
                "Patient.family",
                SearchParamType.String,
                "custom.package@2.0",
                new OverrideInfo(canonical, 1),
                1,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);
    }
}
