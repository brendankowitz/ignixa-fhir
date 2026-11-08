// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

/// <summary>
/// The server write path for the E2E token-search fixture's Observations (<see cref="TokenSearchObservations"/>, compiled from
/// the E2E project), through the registered mediator and validation pipeline at the tenant's default validation depth, so a
/// validator change that would reject the fixture fails here rather than only in the SQL Server E2E run.
/// </summary>
public class TokenSearchFixtureWriteTests
{
    // Guard: every fixture Observation is accepted at the default (Spec) write depth.
    [Fact]
    public async Task GivenTheTokenSearchFixtureObservations_WhenWrittenAtTheDefaultDepth_ThenEachIsAccepted()
    {
        using var harness = new ValidationPipelineRegistrationTests.WriteHarness();
        var schema = harness.Container.Resolve<IFhirVersionContext>().GetBaseSchemaProvider(FhirVersion.R4);
        var observations = TokenSearchObservations.Build(schema, "token-search");

        for (var i = 0; i < observations.Length; i++)
        {
            await harness.SendAsync(observations[i].SerializeToString(), id: $"o{i}");
        }

        harness.Writes.ShouldBe(observations.Length);
    }

    // Regression (documented behaviour change of Task 9b): at 635c8f6 a relative Coding.system inside a choice variant was not
    // checked, so the fixture's former "system1" was accepted at Spec; it is now coding-system-absolute there, as it already
    // was for a non-choice Coding, and as the HL7 validator reports it. Compatibility depth still accepts it.
    [Theory]
    [InlineData(ValidationDepth.Spec, false)]
    [InlineData(ValidationDepth.Compatibility, true)]
    public async Task GivenARelativeCodingSystemInAChoiceVariant_WhenWritten_ThenOnlyCompatibilityAcceptsIt(ValidationDepth depth, bool accepted)
    {
        using var harness = new ValidationPipelineRegistrationTests.WriteHarness();
        const string observation = """
            {"resourceType":"Observation","status":"registered","code":{"coding":[{"system":"http://example.org/test-code","code":"code"}]},"valueCodeableConcept":{"coding":[{"system":"system1","code":"code1"}]}}
            """;

        if (accepted)
        {
            await harness.SendAsync(observation, depth, id: "o1");
        }
        else
        {
            var rejected = await Should.ThrowAsync<ValidationException>(() => harness.SendAsync(observation, depth, id: "o1"));
            rejected.ValidationResult.Issues.ShouldContain(issue => issue.Code == "coding-system-absolute" && issue.Path == "Observation.valueCodeableConcept.coding[0].system");
        }

        harness.Writes.ShouldBe(accepted ? 1 : 0);
    }
}
