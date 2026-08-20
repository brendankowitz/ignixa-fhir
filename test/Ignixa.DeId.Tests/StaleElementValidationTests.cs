// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId;
using Ignixa.DeId.Tests.Utilities;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;

namespace Ignixa.DeId.Tests;

/// <summary>
/// Regression coverage for DeIdContext.Element staleness: before the fix, the root IElement handed
/// to the pipeline was captured once at construction and never re-derived, so OutputFormattingHandler's
/// post-mutation validation inspected the pre-de-identification tree instead of the actual output.
/// </summary>
public class StaleElementValidationTests
{
    [Fact]
    public void GivenObservationMissingStatus_WhenValidatedDirectly_ThenCardinalityCheckFires()
    {
        // Arrange: control case establishing that the validator genuinely enforces
        // Observation.status (1..1) at Minimal depth when given a freshly-derived element -
        // no de-identification or staleness involved.
        var schemaProvider = new R4CoreSchemaProvider();
        var resolver = new StructureDefinitionSchemaResolver(schemaProvider);
        var schema = resolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Observation");
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Observation","id":"obs1","code":{"text":"Test"}}""");
        var element = resource.ToElement(schemaProvider);
        var settings = new ValidationSettings { Depth = ValidationDepth.Minimal, SkipTerminologyValidation = true };

        // Act
        var result = schema!.Validate(element, settings);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Path == "Observation.status");
    }

    [Fact]
    public async Task GivenRedactOfRequiredField_WhenOutputValidationEnabled_ThenValidationObservesTheMutatedTree()
    {
        // Arrange: a rule that redacts Observation.status (1..1, so removing it makes the
        // de-identified output structurally invalid). Output validation is enabled, so it must
        // catch this against the resource as it actually is, not a pre-mutation snapshot.
        var engine = DeIdTestHelpers.CreateR4Engine(
            DeIdTestHelpers.ConfigPath("redact-observation-status-config.json"));
        var json = """{"resourceType":"Observation","id":"obs1","status":"final","code":{"text":"Test"}}""";
        var settings = new RequestOptions { ValidateOutput = true };

        // Act
        var result = await engine.DeidentifyAsync(json, settings);

        // Assert: status was actually redacted, and output validation caught the resulting
        // cardinality violation instead of silently passing on stale (pre-mutation) data.
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("OUTPUT_VALIDATION_FAILED");
        result.Error.Message.ShouldContain("status");
    }
}
