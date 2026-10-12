using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Tests.Features.Conformance;
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
        using var state = await ConformanceEvents.InitializedStateAsync(Activation("published-code", 101));
        using var context = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance);
        context.PublishConformanceDefinitionsSnapshot(
            FhirVersion.R4,
            1,
            context.CreateConformanceDefinitionsSnapshot(FhirVersion.R4, 1, state.CreateSnapshot(), 1));
        state.ApplyAndTrack(ConformanceEvents.At(2, Activation("unpublished-code", 102)));

        var definitions = context.GetSearchParameterDefinitionManager(FhirVersion.R4, 2);

        definitions.TryGetSearchParameter("Patient", "published-code", out _).ShouldBeTrue();
        definitions.TryGetSearchParameter("Patient", "unpublished-code", out _).ShouldBeFalse();
        context.GetDefinitionsHandle(FhirVersion.R4, 2).DefinitionsEventId.ShouldBe(1);
    }

    private static object Activation(string code, int searchParamId) =>
        ConformanceEvents.Activated(
            $"http://example.org/SearchParameter/{code}",
            code,
            searchParamId,
            package: "custom@1.0.0",
            expression: "Patient.name",
            type: SearchParamType.String);
}
