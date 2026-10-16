// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// The resources a Bundle or Parameters resource carries are validated against their own types, with issues rooted at the
/// entry (Task 9b fix round 1, F1). Regressions fail at b0f34da, where a Bundle validated as one resource never looked inside
/// its entries.
/// </summary>
public class EntryResourceValidationTests
{
    // Regression: at b0f34da the entry's bad status, unknown property and string decimal all passed.
    [Theory]
    [InlineData(ValidationDepth.Spec)]
    [InlineData(ValidationDepth.Full)]
    public void GivenABundleWithAnInvalidEntry_WhenValidated_ThenTheEntryIsRejectedAtItsOwnPath(ValidationDepth depth)
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Bundle","id":"b","type":"collection","entry":[
              {"fullUrl":"urn:uuid:11111111-1111-1111-1111-111111111111","resource":{"resourceType":"Patient","id":"p"}},
              {"fullUrl":"urn:uuid:22222222-2222-2222-2222-222222222222","resource":{"resourceType":"Observation","id":"x","status":"bogus","zzz":1,"code":{"text":"x"},"valueQuantity":{"value":"abc"}}}]}
            """, depth);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path == "Bundle.entry[1].resource.status");
        result.Issues.ShouldContain(i => i.Code == "unknown-property" && i.Path == "Bundle.entry[1].resource.zzz");
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path == "Bundle.entry[1].resource.valueQuantity.value");
        result.Issues.ShouldNotContain(i => i.Path.StartsWith("Bundle.entry[0]", StringComparison.Ordinal) && i.Severity == IssueSeverity.Error);
    }

    // Regression: at b0f34da a Parameters resource's resources, a part's included, were not validated.
    [Fact]
    public void GivenParametersCarryingInvalidResources_WhenValidatedAtSpec_ThenEachIsRejectedAtItsOwnPath()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Parameters","parameter":[
              {"name":"a","resource":{"resourceType":"Patient","id":"p","gender":"bogus"}},
              {"name":"b","part":[{"name":"c","resource":{"resourceType":"Observation","status":"bogus","code":{"text":"x"}}}]}]}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path == "Parameters.parameter[0].resource.gender");
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path == "Parameters.parameter[1].part[0].resource.status");
    }

    // Regression: at b0f34da an entry naming no known resource type passed.
    [Fact]
    public void GivenABundleEntryOfAnUnknownResourceType_WhenValidatedAtSpec_ThenItIsAnError()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Bundle","id":"b","type":"collection","entry":[{"fullUrl":"urn:uuid:11111111-1111-1111-1111-111111111111","resource":{"resourceType":"Foo","id":"f"}}]}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "entry-invalid-resourcetype" && i.Path == "Bundle.entry[0].resource");
    }

    // Regression: at b0f34da an entry's reference to a sibling of a type the element does not allow was never checked.
    [Fact]
    public void GivenAnEntryReferencingASiblingOfADisallowedType_WhenValidatedAtSpec_ThenTheTargetTypeIsRejected()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Bundle","id":"b","type":"collection","entry":[
              {"fullUrl":"urn:uuid:11111111-1111-1111-1111-111111111111","resource":{"resourceType":"Organization","id":"o","name":"x"}},
              {"fullUrl":"urn:uuid:22222222-2222-2222-2222-222222222222","resource":{"resourceType":"Observation","id":"x","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:11111111-1111-1111-1111-111111111111"}}}]}
            """, ValidationDepth.Spec);

        result.Issues.ShouldContain(i => i.Code == "reference-target-type" && i.Path == "Bundle.entry[1].resource.subject");
    }

    // Guard: a conformant Bundle whose entries reference each other by urn stays valid, with no reference left unchecked.
    [Fact]
    public void GivenAConformantBundleWithSiblingReferences_WhenValidatedAtSpec_ThenItIsValid()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Bundle","id":"b","type":"collection","entry":[
              {"fullUrl":"urn:uuid:11111111-1111-1111-1111-111111111111","resource":{"resourceType":"Patient","id":"p"}},
              {"fullUrl":"urn:uuid:22222222-2222-2222-2222-222222222222","resource":{"resourceType":"Observation","id":"x","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:11111111-1111-1111-1111-111111111111"}}}]}
            """, ValidationDepth.Spec);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotContain(i => i.Code.StartsWith("reference-target", StringComparison.Ordinal));
    }

    // Regression: at b0f34da an issue inside a resource contained in one of its own type was rooted twice
    // (Observation.contained[0].contained[0].status).
    [Fact]
    public void GivenAResourceContainingOneOfItsOwnType_WhenTheContainedOneIsInvalid_ThenTheIssueIsRootedOnce()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"hasMember":[{"reference":"#c1"}],
             "contained":[{"resourceType":"Observation","id":"c1","status":"bogus","code":{"text":"x"}}]}
            """, ValidationDepth.Spec);

        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Path == "Observation.contained[0].status");
        result.Issues.ShouldNotContain(i => i.Path.Contains("contained[0].contained", StringComparison.Ordinal));
    }
}
