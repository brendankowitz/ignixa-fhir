// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// Reference target types told from more reference forms (Task 9b fix round 1): a CodeableReference's reference (F3), the
/// container reference <c>#</c> (F5) and a conditional reference <c>Type?query</c> (F7). Regressions fail at b0f34da.
/// </summary>
public class ReferenceTargetResolutionTests
{
    private static readonly Lazy<(R5CoreSchemaProvider Schema, ProfileAwareValidationSchemaResolver Resolver)> R5 = new(() =>
    {
        var schema = new R5CoreSchemaProvider();
        return (schema, new ProfileAwareValidationSchemaResolver(new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(schema))));
    });

    // Regression: at b0f34da a CodeableReference's reference was never checked against the element's target types.
    [Fact]
    public void GivenAnR5CodeableReferenceToADisallowedType_WhenValidatedAtSpec_ThenItIsAnError()
    {
        var result = ValidateR5("""
            {"resourceType":"MedicationRequest","id":"m","status":"active","intent":"order","medication":{"reference":{"reference":"Patient/9"}},
             "subject":{"reference":"Patient/1"},"reason":[{"reference":{"reference":"Organization/1"}}]}
            """);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "reference-target-type" && i.Path.StartsWith("MedicationRequest.medication", StringComparison.Ordinal));
        result.Issues.ShouldContain(i => i.Code == "reference-target-type" && i.Path.StartsWith("MedicationRequest.reason[0]", StringComparison.Ordinal));
    }

    // Guard: CodeableReferences to allowed types, or with a concept only, raise no reference issue.
    [Fact]
    public void GivenAnR5CodeableReferenceToAnAllowedType_WhenValidatedAtSpec_ThenNoReferenceIssueIsRaised()
    {
        var result = ValidateR5("""
            {"resourceType":"MedicationRequest","id":"m","status":"active","intent":"order","medication":{"reference":{"reference":"Medication/9"}},
             "subject":{"reference":"Patient/1"},"reason":[{"reference":{"reference":"Condition/1"}},{"concept":{"text":"x"}}]}
            """);

        result.Issues.ShouldNotContain(i => i.Code.StartsWith("reference-", StringComparison.Ordinal));
    }

    // Regression: at b0f34da '#' at the root, the resource itself, was not told, so an Observation as its own subject passed.
    [Fact]
    public void GivenARootReferenceToItself_WhenTheElementDoesNotAllowItsType_ThenItIsAnError()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"subject":{"reference":"#"}}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "reference-target-type" && i.Path == "Observation.subject");
    }

    // Guard: a contained resource's '#' is its container, checked as the container's type: a Patient may be the subject of
    // an Observation it contains, which could not be the subject of itself.
    [Fact]
    public void GivenAContainedReferenceToItsContainer_WhenTheElementAllowsTheContainerType_ThenNoTargetIssueIsRaised()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","contained":[{"resourceType":"Observation","id":"c1","status":"final","code":{"text":"x"},"subject":{"reference":"#"}}]}
            """, ValidationDepth.Spec);

        result.Issues.ShouldNotContain(i => i.Code.StartsWith("reference-target", StringComparison.Ordinal));
    }

    // Regression: at b0f34da a conditional reference was undeterminable, so a disallowed type passed.
    [Fact]
    public void GivenAConditionalReferenceToADisallowedType_WhenValidatedAtSpec_ThenItIsAnError()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"subject":{"reference":"Organization?identifier=http://x|1"}}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "reference-target-type" && i.Path == "Observation.subject");
    }

    // Regression: at b0f34da a conditional reference to an allowed type was reported as undeterminable.
    [Fact]
    public void GivenAConditionalReferenceToAnAllowedType_WhenValidatedAtSpec_ThenItIsCheckedAndAccepted()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"subject":{"reference":"Patient?identifier=http://x|1"}}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotContain(i => i.Code.StartsWith("reference-target", StringComparison.Ordinal));
    }

    private static ValidationResult ValidateR5(string json)
    {
        var (schema, resolver) = R5.Value;
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(schema);
        return resolver.ResolveForElement(element)!.Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec });
    }
}
