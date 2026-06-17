// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using System.Text.Json.Nodes;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for the <see cref="GenerationDensity"/> axis on <see cref="SchemaBasedFhirResourceFaker"/>.
/// Verifies that Minimal preserves required-only behavior, Maximal populates optional elements,
/// Realistic is equivalent to Minimal, and Maximal generation terminates and validates.
/// </summary>
public class GenerationDensityTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    [Fact]
    public void GivenDefaultDensity_WhenConstructed_ThenIsMinimal()
    {
        var faker = new SchemaBasedFhirResourceFaker(_schemaProvider);

        faker.Density.ShouldBe(GenerationDensity.Minimal);
    }

    [Fact]
    public void GivenMinimalDensity_WhenGeneratingPatient_ThenOptionalGenderIsAbsent()
    {
        var faker = new SchemaBasedFhirResourceFaker(_schemaProvider) { Density = GenerationDensity.Minimal };

        var patient = faker.Generate("Patient");

        patient.MutableNode["gender"].ShouldBeNull(
            "Patient.gender is optional (0..1) and should be omitted under Minimal density");
    }

    [Fact]
    public void GivenMinimalDensity_WhenGeneratingObservation_ThenOptionalBodySiteIsAbsent()
    {
        var faker = new SchemaBasedFhirResourceFaker(_schemaProvider) { Density = GenerationDensity.Minimal };

        var observation = faker.Generate("Observation");

        observation.MutableNode["bodySite"].ShouldBeNull(
            "Observation.bodySite is optional and should be omitted under Minimal density");
    }

    [Fact]
    public void GivenMaximalDensity_WhenGeneratingPatient_ThenOptionalGenderIsPresent()
    {
        var faker = new SchemaBasedFhirResourceFaker(_schemaProvider, seed: 5) { Density = GenerationDensity.Maximal };

        var patient = faker.Generate("Patient");

        patient.MutableNode["gender"].ShouldNotBeNull(
            "Patient.gender is optional and should be populated under Maximal density");
    }

    [Fact]
    public void GivenMaximalDensity_WhenGeneratingObservation_ThenOptionalBodySiteIsPresent()
    {
        var faker = new SchemaBasedFhirResourceFaker(_schemaProvider, seed: 5) { Density = GenerationDensity.Maximal };

        var observation = faker.Generate("Observation");

        observation.MutableNode["bodySite"].ShouldNotBeNull(
            "Observation.bodySite is optional and should be populated under Maximal density");
    }

    [Fact]
    public void GivenMaximalDensity_WhenGeneratingPatient_ThenPopulatesMoreElementsThanMinimal()
    {
        var minimal = new SchemaBasedFhirResourceFaker(_schemaProvider, seed: 5) { Density = GenerationDensity.Minimal }
            .Generate("Patient");
        var maximal = new SchemaBasedFhirResourceFaker(_schemaProvider, seed: 5) { Density = GenerationDensity.Maximal }
            .Generate("Patient");

        var minimalKeys = ((JsonObject)minimal.MutableNode).Count;
        var maximalKeys = ((JsonObject)maximal.MutableNode).Count;

        maximalKeys.ShouldBeGreaterThan(minimalKeys,
            "Maximal density should populate strictly more top-level elements than Minimal");
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("Observation")]
    [InlineData("Questionnaire")]
    [InlineData("Bundle")]
    public void GivenMaximalDensity_WhenGeneratingNestedTypes_ThenTerminatesWithoutThrowing(string resourceType)
    {
        var faker = new SchemaBasedFhirResourceFaker(_schemaProvider, seed: 7) { Density = GenerationDensity.Maximal };

        var resource = Should.NotThrow(() => faker.Generate(resourceType));

        resource.ShouldNotBeNull();
        resource.ResourceType.ShouldBe(resourceType);
        MeasureMaxDepth(resource.MutableNode).ShouldBeLessThanOrEqualTo(50,
            "Generated tree nesting depth should be finite and bounded");
    }

    [Fact]
    public void GivenRealisticDensity_WhenGeneratingPatient_ThenBehavesIdenticallyToMinimal()
    {
        var minimal = new SchemaBasedFhirResourceFaker(_schemaProvider, seed: 11) { Density = GenerationDensity.Minimal }
            .Generate("Patient");
        var realistic = new SchemaBasedFhirResourceFaker(_schemaProvider, seed: 11) { Density = GenerationDensity.Realistic }
            .Generate("Patient");

        RemoveNonDeterministicFields(minimal.MutableNode);
        RemoveNonDeterministicFields(realistic.MutableNode);

        minimal.MutableNode.ToJsonString().ShouldBe(realistic.MutableNode.ToJsonString(),
            "Realistic density currently behaves identically to Minimal (required-only) — full JSON content should match");
    }

    [Fact]
    public void GivenRealisticDensity_WhenGeneratingObservation_ThenBehavesIdenticallyToMinimal()
    {
        var minimal = new SchemaBasedFhirResourceFaker(_schemaProvider, seed: 12) { Density = GenerationDensity.Minimal }
            .Generate("Observation");
        var realistic = new SchemaBasedFhirResourceFaker(_schemaProvider, seed: 12) { Density = GenerationDensity.Realistic }
            .Generate("Observation");

        RemoveNonDeterministicFields(minimal.MutableNode);
        RemoveNonDeterministicFields(realistic.MutableNode);

        minimal.MutableNode.ToJsonString().ShouldBe(realistic.MutableNode.ToJsonString(),
            "Realistic density currently behaves identically to Minimal (required-only) — full JSON content should match");
    }

    [Fact]
    public void GivenMaximalDensity_WhenGeneratingPatient_ThenStillValidates()
    {
        var faker = new SchemaBasedFhirResourceFaker(_schemaProvider, seed: 5) { Density = GenerationDensity.Maximal };

        var patient = faker.Generate("Patient");
        var errors = ValidateResource(patient).Issues
            .Where(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal)
            .ToList();

        errors.ShouldBeEmpty(
            $"Maximal Patient should remain valid. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
    }

    private ValidationResult ValidateResource(ResourceJsonNode resource)
    {
        var resolver = new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(_schemaProvider));
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resource.ResourceType}";
        var schema = resolver.GetSchema(canonicalUrl)
            ?? throw new InvalidOperationException($"Schema not found for {resource.ResourceType}");

        var sourceNode = JsonNodeSourceNode.Create(resource.MutableNode);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        return schema.Validate(sourceNode.ToElement(_schemaProvider), settings, new ValidationState());
    }

    private static int MeasureMaxDepth(JsonNode? node)
    {
        if (node is JsonObject obj)
            return 1 + (obj.Any() ? obj.Max(kvp => MeasureMaxDepth(kvp.Value)) : 0);
        if (node is JsonArray arr)
            return 1 + (arr.Any() ? arr.Max(item => MeasureMaxDepth(item)) : 0);
        return 1;
    }

    private static void RemoveNonDeterministicFields(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["meta"] is JsonObject metaObj)
            {
                metaObj.Remove("lastUpdated");
            }

            foreach (var kvp in obj.ToList())
            {
                RemoveNonDeterministicFields(kvp.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                RemoveNonDeterministicFields(item);
            }
        }
    }
}
