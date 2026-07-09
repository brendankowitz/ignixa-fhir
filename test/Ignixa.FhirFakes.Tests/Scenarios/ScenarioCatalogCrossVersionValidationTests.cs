// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Shouldly;
using Ignixa.Serialization.TestSupport;

namespace Ignixa.FhirFakes.Tests.Scenarios;

/// <summary>
/// Runs every catalog-discovered predefined scenario (the actual 34 named clinical pathways, not a
/// hand-picked subset) against every FHIR schema version and validates every generated resource. This
/// is distinct from ComprehensiveValidationTests, which validates a hand-rolled set of ~13 generic
/// scenarios, not the real named pathways a consumer actually calls by id.
/// </summary>
[Collection(CatalogRegistrationGroup.Name)]
public class ScenarioCatalogCrossVersionValidationTests
{
    public static IEnumerable<object[]> SchemaProviders()
    {
        yield return [new STU3CoreSchemaProvider()];
        yield return [new R4CoreSchemaProvider()];
        yield return [new R4BCoreSchemaProvider()];
        yield return [new R5CoreSchemaProvider()];
        yield return [new R6CoreSchemaProvider()];
    }

    [Theory]
    [MemberData(nameof(SchemaProviders))]
    public void GivenEveryPredefinedScenario_WhenGeneratedAndValidated_ThenAllResourcesPassSchemaValidation(IFhirSchemaProvider schemaProvider)
    {
        var scenarioIds = ScenarioCatalog.GetAll().Select(s => s.Id).ToList();
        var scenariosWithFailures = new HashSet<string>();
        var invocationFailures = new List<string>();
        var guardSkips = new List<string>();
        var errorCategoryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var totalResourceFailures = 0;

        foreach (var scenario in ScenarioCatalog.GetAll())
        {
            ScenarioContext context;
            try
            {
                context = ScenarioCatalog.Invoke(scenario, schemaProvider);
            }
            catch (ScenarioInvocationException ex) when (ex.InnerException is InvalidOperationException
                && ex.InnerException.Message.StartsWith("Guard condition not met", StringComparison.Ordinal))
            {
                // A GuardState halting a probabilistic scenario (e.g. only proceeding to full
                // treatment when a randomly-rolled clinical score meets a threshold) is intentional
                // domain modeling, not a bug — the scenario is designed to sometimes not apply.
                guardSkips.Add($"{scenario.Id}: {ex.InnerException.Message}");
                continue;
            }
            catch (ScenarioInvocationException ex)
            {
                invocationFailures.Add($"{scenario.Id}: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
                continue;
            }

            foreach (var resource in context.AllResources)
            {
                var errors = ValidateAndCollectErrors(resource.MutableNode(), schemaProvider);
                if (errors.Count == 0)
                {
                    continue;
                }

                scenariosWithFailures.Add(scenario.Id);
                totalResourceFailures++;
                foreach (var error in errors)
                {
                    var category = $"{resource.ResourceType}: {NormalizeArrayIndices(error)}";
                    errorCategoryCounts[category] = errorCategoryCounts.GetValueOrDefault(category) + 1;
                }
            }
        }

        var report = string.Join("\n", new[]
        {
            $"=== {schemaProvider.FullVersion} ===",
            $"Scenarios: {scenarioIds.Count} total, {scenariosWithFailures.Count} with >=1 failing resource, {invocationFailures.Count} threw during invocation, {guardSkips.Count} skipped by guard.",
            $"Total failing resources: {totalResourceFailures}.",
            invocationFailures.Count > 0 ? "Invocation failures:\n  " + string.Join("\n  ", invocationFailures) : null,
            guardSkips.Count > 0 ? "Guard skips (expected, not failures):\n  " + string.Join("\n  ", guardSkips) : null,
            errorCategoryCounts.Count > 0
                ? "Distinct error categories (count):\n  " + string.Join("\n  ", errorCategoryCounts.OrderByDescending(kv => kv.Value).Select(kv => $"[{kv.Value}] {kv.Key}"))
                : null,
        }.Where(s => s is not null));

        (totalResourceFailures == 0 && invocationFailures.Count == 0).ShouldBeTrue(report);
    }

    private static string NormalizeArrayIndices(string error) =>
        System.Text.RegularExpressions.Regex.Replace(error, @"\[\d+\]", "[n]");

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
