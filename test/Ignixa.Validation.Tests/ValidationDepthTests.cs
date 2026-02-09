// <copyright file="ValidationDepthTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Tests for ValidationDepth ordering, specifically Compatibility mode behavior.
/// Regression tests for Bug #210-6: Compatibility=3 runs ALL checks because schema uses >= comparisons.
/// </summary>
public class ValidationDepthTests
{
    private readonly ISchema _schema;
    private readonly StructureDefinitionSchemaBuilder _builder;

    public ValidationDepthTests()
    {
        _schema = new R4CoreSchemaProvider();
        _builder = new StructureDefinitionSchemaBuilder();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenCompatibilityDepth_WhenValidating_ThenDoesNotRunProfileChecks()
    {
        // Arrange - Bug #210-6: Compatibility=3 is > Full=2, so >= Full is true,
        // causing profile checks (FHIRPath invariants) to run in Compatibility mode.
        // Compatibility should be more lenient than Spec, not run MORE checks.
        var typeDefinition = _schema.GetTypeDefinition("Observation");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Build a minimally valid Observation (intentionally missing some fields to trigger invariants)
        var json = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final",
                "code": {"text": "test"}
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        // Validate at Full depth
        var fullSettings = new ValidationSettings { Depth = ValidationDepth.Full };
        var fullResult = schema.Validate(element, fullSettings);

        // Validate at Compatibility depth
        var compatSettings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var compatResult = schema.Validate(element, compatSettings);

        // Assert - Compatibility should run FEWER or EQUAL checks than Full, not more
        // If Compatibility runs profile checks (FHIRPath invariants), it has MORE issues than Full,
        // which is wrong because Compatibility should be the most lenient mode
        compatResult.Issues.Count.ShouldBeLessThanOrEqualTo(fullResult.Issues.Count,
            "Compatibility depth should not produce more validation issues than Full depth");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenCompatibilityDepth_WhenValidating_ThenRunsFewerChecksThanFullDepth()
    {
        // Arrange - Test that Compatibility mode (3) doesn't run profile checks despite being > Full (2)
        // The fix in ValidationSchema.cs uses == Full instead of >= Full to avoid running profile checks
        // in Compatibility mode
        var typeDefinition = _schema.GetTypeDefinition("Observation");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        // Build an Observation resource
        var json = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final",
                "code": {"text": "test"}
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        // Validate at Full depth (may run profile checks if any exist)
        var fullSettings = new ValidationSettings { Depth = ValidationDepth.Full };
        var fullResult = schema.Validate(element, fullSettings);

        // Validate at Compatibility depth (should NOT run profile checks)
        var compatSettings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var compatResult = schema.Validate(element, compatSettings);

        // After fix: Compatibility mode should not run profile checks,
        // so it produces fewer or equal issues than Full depth
        // This test passes as long as Compatibility doesn't run MORE checks than Full
        compatResult.Issues.Count.ShouldBeLessThanOrEqualTo(fullResult.Issues.Count,
            "Compatibility depth should not run more checks than Full depth");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenCompatibilityDepth_WhenComparedToSpec_ThenRunsSameOrFewerChecks()
    {
        // Arrange - Compatibility should be at most as strict as Spec
        var typeDefinition = _schema.GetTypeDefinition("Patient");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        var json = JsonNode.Parse("""
            {
                "resourceType": "Patient",
                "id": "123",
                "active": true
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        // Validate at Spec depth
        var specSettings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var specResult = schema.Validate(element, specSettings);

        // Validate at Compatibility depth
        var compatSettings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var compatResult = schema.Validate(element, compatSettings);

        // Assert - Compatibility should produce same or fewer issues than Spec
        compatResult.Issues.Count.ShouldBeLessThanOrEqualTo(specResult.Issues.Count,
            "Compatibility depth should not produce more validation issues than Spec depth");
    }
}
