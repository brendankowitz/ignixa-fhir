// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Specification.Generated;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using SearchParamInfo = Ignixa.Search.Models.SearchParameterInfo;

namespace Ignixa.Application.Tests.Search.Definition;

public class CompositeSearchParameterDefinitionManagerReferenceIdentifierTests
{
    [Fact]
    public async Task GivenPackageReferenceParameter_WhenManagerInitializes_ThenDerivedParameterResolvesAndIsEnumerated()
    {
        var state = new ConformanceState();
        var activated = new SearchParameterActivated(
            Canonical: "http://example.org/fhir/SearchParameter/encounter-care-manager",
            Code: "care-manager",
            ResourceType: "Encounter",
            Expression: "Encounter.participant.individual",
            ParamType: SearchParamType.Reference,
            SourcePackage: "example.package@1.0.0",
            Overrides: null,
            SearchParamId: 1,
            TargetResourceTypes: ["Practitioner"],
            Components: null,
            Name: "care-manager",
            Description: null);
        var sourceEvent = new SourceEvent(
            EventId: 1,
            StreamId: "package/example.package",
            EventType: nameof(SearchParameterActivated),
            Data: activated,
            Timestamp: DateTimeOffset.UtcNow);
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(ReadEvents(sourceEvent));
        await state.InitializeFromEventsAsync(eventStore, CancellationToken.None);
        var baseManager = new SearchParameterDefinitionManager(
            new R4CoreSchemaProvider(),
            NullLogger<SearchParameterDefinitionManager>.Instance);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager,
            state,
            "4.0.1",
            NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions());

        await manager.InitializeAsync(CancellationToken.None);

        SearchParamInfo original = manager.GetSearchParameter("Encounter", "care-manager");
        ReferenceIdentifierSearchParameterFactory.TryResolve(manager, original, out SearchParamInfo derived).ShouldBeTrue();
        manager.GetSearchParameter("Encounter", "care-manager:identifier").ShouldBeSameAs(derived);
        manager.AllSearchParameters.ShouldContain(derived);
    }

    [Fact]
    public async Task GivenPackageReferenceParameter_WhenManagerInitializes_ThenCompositeHashIncludesDerivedParameter()
    {
        var state = new ConformanceState();
        var activated = new SearchParameterActivated(
            Canonical: "http://example.org/fhir/SearchParameter/encounter-care-manager",
            Code: "care-manager",
            ResourceType: "Encounter",
            Expression: "Encounter.participant.individual",
            ParamType: SearchParamType.Reference,
            SourcePackage: "example.package@1.0.0",
            Overrides: null,
            SearchParamId: 1,
            TargetResourceTypes: ["Practitioner"],
            Components: null,
            Name: "care-manager",
            Description: null);
        var sourceEvent = new SourceEvent(
            EventId: 1,
            StreamId: "package/example.package",
            EventType: nameof(SearchParameterActivated),
            Data: activated,
            Timestamp: DateTimeOffset.UtcNow);
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(ReadEvents(sourceEvent));
        await state.InitializeFromEventsAsync(eventStore, CancellationToken.None);
        var baseManager = new SearchParameterDefinitionManager(
            new R4CoreSchemaProvider(),
            NullLogger<SearchParameterDefinitionManager>.Instance);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager,
            state,
            "4.0.1",
            NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions());

        await manager.InitializeAsync(CancellationToken.None);

        string expectedHash = manager.GetSearchParameters("Encounter").CalculateSearchParameterHash();
        manager.GetSearchParameterHashForResourceType("Encounter").ShouldBe(expectedHash);
        manager.SearchParameterHashMap["Encounter"].ShouldBe(expectedHash);
        expectedHash.ShouldNotBe(baseManager.GetSearchParameterHashForResourceType("Encounter"));
    }

    private static async IAsyncEnumerable<SourceEvent> ReadEvents(SourceEvent sourceEvent)
    {
        await Task.CompletedTask;
        yield return sourceEvent;
    }
}
