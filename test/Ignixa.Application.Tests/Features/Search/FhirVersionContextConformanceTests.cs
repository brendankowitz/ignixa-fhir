using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Search;

public class FhirVersionContextConformanceTests
{
    [Fact]
    public async Task GivenLiveStateAdvancedPastThePublishedProjection_WhenAnUnpublishedTenantResolves_ThenItUsesThePublishedProjection()
    {
        using var state = new ConformanceState();
        await state.InitializeFromEventsAsync(TestConformanceRefresher.EmptyEventStore(), CancellationToken.None);
        state.ApplyAndTrack(Activation(1, "published-code"));
        using var context = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance);
        context.PublishConformanceDefinitionsSnapshot(
            FhirVersion.R4,
            1,
            context.CreateConformanceDefinitionsSnapshot(FhirVersion.R4, 1, state.CreateSnapshot(), 1));
        state.ApplyAndTrack(Activation(2, "unpublished-code"));

        var definitions = context.GetSearchParameterDefinitionManager(FhirVersion.R4, 2);

        definitions.TryGetSearchParameter("Patient", "published-code", out _).ShouldBeTrue();
        definitions.TryGetSearchParameter("Patient", "unpublished-code", out _).ShouldBeFalse();
        context.GetDefinitionsHandle(FhirVersion.R4, 2).DefinitionsEventId.ShouldBe(1);
    }

    private static SourceEvent Activation(long eventId, string code) =>
        new(
            eventId,
            "package:custom@1.0.0",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                $"http://example.org/SearchParameter/{code}",
                code,
                "Patient",
                "Patient.name",
                SearchParamType.String,
                "custom@1.0.0",
                null,
                (int)eventId + 100,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);
}
