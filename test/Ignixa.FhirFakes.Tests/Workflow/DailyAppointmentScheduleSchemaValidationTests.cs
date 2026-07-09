// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

/// <summary>
/// Validates every resource in the DailyAppointmentSchedule graph against each FHIR version's real
/// schema. This is the automated guard the STU3 <c>Encounter.appointment</c> cardinality regression
/// lacked: a version-shaped bug now fails here instead of only under a manual <c>--validate</c> run.
/// </summary>
public class DailyAppointmentScheduleSchemaValidationTests
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
    public void GivenSchemaProvider_WhenGeneratingDailyAppointmentSchedule_ThenEveryResourceValidates(IFhirSchemaProvider schemaProvider)
    {
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var result = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, new WorkflowScenarioOptions { Seed = 5 });

        result.Graph.AllResources.Count.ShouldBeGreaterThan(0);
        foreach (var resource in result.Graph.AllResources)
        {
            var errors = ValidateAndCollectErrors(((IMutableJsonNode)resource).MutableNode, schemaProvider);
            errors.ShouldBeEmpty(
                $"{schemaProvider.FullVersion} {resource.ResourceType}/{resource.Id} produced validation errors: {string.Join(" | ", errors)}");
        }
    }

    private static IReadOnlyList<string> ValidateAndCollectErrors(JsonNode resourceNode, IFhirSchemaProvider schemaProvider)
    {
        var sourceNode = JsonNodeSourceNode.Create(resourceNode);
        var resourceType = sourceNode.ResourceType ?? sourceNode.Name;

        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
        var resolver = new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(schemaProvider));
        var schema = resolver.GetSchema(canonicalUrl);
        schema.ShouldNotBeNull($"schema not found for {resourceType}");

        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState();
        var element = sourceNode.ToElement(schemaProvider);
        var validationResult = schema!.Validate(element, settings, state);

        return validationResult.Issues
            .Where(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal)
            .Select(i => $"@{i.Path}: {i.Message}")
            .ToList();
    }
}
