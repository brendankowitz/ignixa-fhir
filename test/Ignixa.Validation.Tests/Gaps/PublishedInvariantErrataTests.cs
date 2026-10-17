// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// ref-1 and bdl-8 as the HL7 validator's FHIRPathExpressionFixer corrects them, per version (Task 9b fix round 2, N2).
/// Regressions fail at 758e490, where R4's ref-1 rejected <c>#</c>, STU3's read the contained pool from a contained
/// resource itself, and R4's and STU3's bdl-8 rejected an entry with no fullUrl.
/// </summary>
public class PublishedInvariantErrataTests
{
    private const string Narrative = """ "text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>"}, """;

    // Regression: at 758e490 R4's ref-1 failed '#' at the root (the resource itself, F5) and from a contained resource.
    [Theory]
    [InlineData("""{"resourceType":"Patient","id":"p",@TEXT"link":[{"other":{"reference":"#"},"type":"seealso"}]}""")]
    [InlineData("""{"resourceType":"Observation","id":"o",@TEXT"status":"final","code":{"text":"x"},"hasMember":[{"reference":"#c1"}],"contained":[{"resourceType":"Observation","id":"c1","status":"final","code":{"text":"x"},"hasMember":[{"reference":"#"}],"derivedFrom":[{"reference":"#c2"}]},{"resourceType":"Observation","id":"c2","status":"final","code":{"text":"x"}}]}""")]
    public void GivenAnR4ReferenceToTheContainerOrItself_WhenValidatedAtFull_ThenRef1Passes(string json)
    {
        var result = JsonResourceValidator.Validate<R4CoreSchemaProvider>(Expand(json), ValidationDepth.Full);

        result.Issues.ShouldNotContain(i => i.Code == "ref-1");
    }

    // Regression: at 758e490 STU3's ref-1 read %resource.contained, empty inside a contained resource, so a contained
    // resource's reference to a sibling ('#c2') or to its container ('#') failed.
    [Fact]
    public void GivenAnStu3ContainedResourceReferencingASiblingAndItsContainer_WhenValidatedAtFull_ThenRef1Passes()
    {
        var result = JsonResourceValidator.Validate<STU3CoreSchemaProvider>(Expand("""
            {"resourceType":"Observation","id":"o",@TEXT"status":"final","code":{"text":"x"},"related":[{"target":{"reference":"#c1"}}],
             "contained":[{"resourceType":"Observation","id":"c1","status":"final","code":{"text":"x"},"related":[{"target":{"reference":"#c2"}},{"target":{"reference":"#"}}]},
                          {"resourceType":"Observation","id":"c2","status":"final","code":{"text":"x"}}]}
            """), ValidationDepth.Full);

        result.Issues.ShouldNotContain(i => i.Code == "ref-1");
    }

    // Guard: a local reference naming nothing contained still fails ref-1 in every version.
    [Theory]
    [InlineData("stu3")]
    [InlineData("r4")]
    [InlineData("r4b")]
    [InlineData("r5")]
    public void GivenADanglingLocalReference_WhenValidatedAtFull_ThenRef1FailsInEveryVersion(string version)
    {
        var json = Expand("""{"resourceType":"Observation","id":"o",@TEXT"status":"final","code":{"text":"x"},"subject":{"reference":"#x"},"contained":[{"resourceType":"Patient","id":"c1"}]}""");

        var result = version switch
        {
            "stu3" => JsonResourceValidator.Validate<STU3CoreSchemaProvider>(json, ValidationDepth.Full),
            "r4" => JsonResourceValidator.Validate<R4CoreSchemaProvider>(json, ValidationDepth.Full),
            "r4b" => JsonResourceValidator.Validate<R4BCoreSchemaProvider>(json, ValidationDepth.Full),
            _ => JsonResourceValidator.Validate<R5CoreSchemaProvider>(json, ValidationDepth.Full),
        };

        result.Issues.ShouldContain(i => i.Code == "ref-1" && i.Path == "Observation.subject");
    }

    // Regression: at 758e490 R4's and STU3's bdl-8 failed an entry with no fullUrl (a transaction-response outcome).
    [Theory]
    [InlineData("stu3")]
    [InlineData("r4")]
    public void GivenABundleEntryWithoutAFullUrl_WhenValidatedAtFull_ThenBdl8Passes(string version)
    {
        const string json = """
            {"resourceType":"Bundle","id":"b","type":"transaction-response","entry":[{"response":{"status":"201"}}]}
            """;

        var result = version == "stu3"
            ? JsonResourceValidator.Validate<STU3CoreSchemaProvider>(json, ValidationDepth.Full)
            : JsonResourceValidator.Validate<R4CoreSchemaProvider>(json, ValidationDepth.Full);

        result.Issues.ShouldNotContain(i => i.Code == "bdl-8");
    }

    // Guard: a version-specific fullUrl still fails bdl-8.
    [Fact]
    public void GivenABundleEntryWithAVersionSpecificFullUrl_WhenValidatedAtFull_ThenBdl8Fails()
    {
        var result = JsonResourceValidator.Validate<R4CoreSchemaProvider>("""
            {"resourceType":"Bundle","id":"b","type":"collection","entry":[{"fullUrl":"http://example.org/fhir/Patient/1/_history/2","resource":{"resourceType":"Patient","id":"1"}}]}
            """, ValidationDepth.Full);

        result.Issues.ShouldContain(i => i.Code == "bdl-8");
    }

    private static string Expand(string json) => json.Replace("@TEXT", Narrative, StringComparison.Ordinal);
}
