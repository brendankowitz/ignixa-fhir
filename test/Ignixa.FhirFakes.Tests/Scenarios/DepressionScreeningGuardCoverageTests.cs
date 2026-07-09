// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Predefined;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.FhirFakes.Tests.Scenarios;

/// <summary>
/// <see cref="ScenarioCatalogCrossVersionValidationTests"/> treats a <c>GuardState</c>-triggered
/// halt in <see cref="MentalHealthTreatmentScenario.DepressionScreeningAndTreatment"/> as an
/// expected skip (the scenario only proceeds to diagnosis/treatment when a probabilistically-rolled
/// PHQ-9 score is >= 10, a ~30% chance per invocation). That test never guarantees the guarded tail
/// — MDD diagnosis, GAD-7/suicide-risk observations, SSRI order, psychotherapy procedure, follow-up
/// encounters — is ever actually exercised and schema-validated. This test repeatedly invokes the
/// scenario until the guard passes at least once, then validates that run's resources directly.
/// </summary>
public class DepressionScreeningGuardCoverageTests
{
    private const int MaxAttempts = 50;

    private readonly ITestOutputHelper _output;

    public DepressionScreeningGuardCoverageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GivenDepressionScreeningScenario_WhenGuardEventuallyPasses_ThenGuardedTailResourcesPassSchemaValidation()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var guardPassed = false;
        var attemptsUsed = 0;

        for (var attempt = 1; attempt <= MaxAttempts && !guardPassed; attempt++)
        {
            attemptsUsed = attempt;

            ScenarioContext context;
            try
            {
                context = schemaProvider.DepressionScreeningAndTreatment();
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("Guard condition not met", StringComparison.Ordinal))
            {
                continue;
            }

            guardPassed = true;

            var failures = new List<string>();
            foreach (var resource in context.AllResources)
            {
                var errors = ValidateAndCollectErrors(((IMutableJsonNode)resource).MutableNode, schemaProvider);
                failures.AddRange(errors.Select(e => $"{resource.ResourceType}: {e}"));
            }

            failures.ShouldBeEmpty(
                $"Guard passed on attempt {attempt}; resources from the guarded tail should be schema-valid.\n" +
                string.Join("\n", failures));
        }

        _output.WriteLine($"Guard passed after {attemptsUsed} attempt(s) (of {MaxAttempts} max).");

        // If the guard never passes across MaxAttempts, that itself is a finding worth surfacing
        // distinctly from a schema-validation failure: either the probability distribution or the
        // guard's threshold comparison is broken, not just an unlucky run.
        guardPassed.ShouldBeTrue(
            $"Guard condition on PHQ-9 >= 10 (~30% probability per run) never passed in {MaxAttempts} attempts. " +
            "This suggests the probabilistic distribution or the guard's comparison logic is broken, " +
            "not just statistical bad luck.");
    }

    private static IReadOnlyList<string> ValidateAndCollectErrors(JsonNode resourceNode, IFhirSchemaProvider schemaProvider)
    {
        var sourceNode = JsonNodeSourceNode.Create(resourceNode);
        var resourceType = sourceNode.ResourceType ?? sourceNode.Name;

        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
        var resolver = new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(schemaProvider));
        var schema = resolver.GetSchema(canonicalUrl);
        if (schema is null)
        {
            return [$"no schema found for resource type '{resourceType}'"];
        }

        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState();
        var element = sourceNode.ToElement(schemaProvider);
        var validationResult = schema.Validate(element, settings, state);

        return validationResult.Issues
            .Where(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal)
            .Select(i => $"@{i.Path}: {i.Message}")
            .ToList();
    }
}
