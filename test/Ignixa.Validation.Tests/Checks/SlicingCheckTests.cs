// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Unit tests for <see cref="SlicingCheck"/>: slice assignment by each supported discriminator type
/// (value, exists, type), per-slice cardinality accounting, closed / openAtEnd rule enforcement,
/// ordered slicing, the Full-tier depth gate, the profile-discriminator deferral, and the runtime
/// deferral when a discriminator expression cannot be evaluated.
/// </summary>
public sealed class SlicingCheckTests
{
    private static readonly ValidationSettings FullDepth = new() { Depth = ValidationDepth.Full };

    private static IElement PatientWith(string extensionsJson)
    {
        var json = JsonNode.Parse($$"""{"resourceType":"Patient","extension":{{extensionsJson}}}""");
        return JsonNodeSourceNode.Create(json).ToElement(TestSchemaProvider.GetR4Schema());
    }

    private static SlicingMetadata ValueUrlSlicing(SlicingRules rules, params SliceDefinition[] slices)
        => new(
            new[] { new DiscriminatorDefinition(DiscriminatorType.Value, "url") },
            rules,
            ordered: false,
            slices);

    private static SliceDefinition ValueUrlSlice(string name, int min, int? max, string expectedUrl)
        => new(name, min, max, new[] { new SliceDiscriminatorValue(DiscriminatorType.Value, "url", expectedUrl) });

    [Fact]
    public void GivenExtensionMatchingSliceWithinMax_WhenValidating_ThenSucceeds()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"Asian"}]""");
        var metadata = ValueUrlSlicing(SlicingRules.Closed, ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTwoExtensionsForSingleCardinalitySlice_WhenValidating_ThenReportsSliceCardinalityError()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"A"},{"url":"http://x/race","valueString":"B"}]""");
        var metadata = ValueUrlSlicing(SlicingRules.Open, ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("slicing-cardinality");
        issue.Message.ShouldContain("race");
        issue.Message.ShouldContain("at most 1");
    }

    [Fact]
    public void GivenUnknownUrlUnderClosedSlicing_WhenValidating_ThenReportsUnmatchedError()
    {
        var element = PatientWith("""[{"url":"http://x/unknown","valueString":"?"}]""");
        var metadata = ValueUrlSlicing(SlicingRules.Closed, ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("slicing-unmatched");
    }

    [Fact]
    public void GivenUnknownUrlUnderOpenSlicing_WhenValidating_ThenAcceptsAsDefaultBucket()
    {
        var element = PatientWith("""[{"url":"http://x/unknown","valueString":"?"}]""");
        var metadata = ValueUrlSlicing(SlicingRules.Open, ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenMandatorySliceAbsent_WhenValidating_ThenReportsMinCardinalityError()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"A"}]""");
        var metadata = ValueUrlSlicing(
            SlicingRules.Open,
            ValueUrlSlice("race", 0, 1, "http://x/race"),
            ValueUrlSlice("birthsex", 1, 1, "http://x/birthsex"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("slicing-cardinality");
        issue.Message.ShouldContain("birthsex");
        issue.Message.ShouldContain("at least 1");
    }

    [Fact]
    public void GivenExistsDiscriminator_WhenPathPresent_ThenAssignsToSlice()
    {
        var element = PatientWith("""[{"url":"http://x/a","valueString":"present"}]""");
        var metadata = new SlicingMetadata(
            new[] { new DiscriminatorDefinition(DiscriminatorType.Exists, "valueString") },
            SlicingRules.Closed,
            ordered: false,
            new[] { new SliceDefinition("hasValueString", 1, 1, new[] { new SliceDiscriminatorValue(DiscriminatorType.Exists, "valueString", null) }) });
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenSpecDepth_WhenValidating_ThenSkippedRegardlessOfViolations()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"A"},{"url":"http://x/race","valueString":"B"}]""");
        var metadata = ValueUrlSlicing(SlicingRules.Closed, ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec }, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenProfileDiscriminator_WhenValidating_ThenDeferredWithInformationOnly()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"A"},{"url":"http://x/race","valueString":"B"}]""");
        var metadata = new SlicingMetadata(
            new[] { new DiscriminatorDefinition(DiscriminatorType.Profile, "$this") },
            SlicingRules.Closed,
            ordered: false,
            new[] { new SliceDefinition("race", 0, 1, new[] { new SliceDiscriminatorValue(DiscriminatorType.Profile, "$this", "http://x/race") }) });
        var check = new SlicingCheck("extension", metadata);

        check.IsDeferred.ShouldBeTrue();
        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldAllBe(i => i.Severity == IssueSeverity.Information);
    }

    [Fact]
    public void GivenTypeDiscriminator_WhenBucketingByInstanceType_ThenAssignsEachToMatchingSlice()
    {
        var element = PatientWith("""
        [
          {"url":"http://x/a","valueString":"s"},
          {"url":"http://x/b","valueCode":"c"}
        ]
        """);
        var metadata = new SlicingMetadata(
            new[] { new DiscriminatorDefinition(DiscriminatorType.Type, "value") },
            SlicingRules.Closed,
            ordered: false,
            new[]
            {
                new SliceDefinition("stringValue", 1, 1, new[] { new SliceDiscriminatorValue(DiscriminatorType.Type, "value", "string") }),
                new SliceDefinition("codeValue", 1, 1, new[] { new SliceDiscriminatorValue(DiscriminatorType.Type, "value", "code") }),
            });
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenOpenAtEndSlicing_WhenUnmatchedContentTrailsMatchedSlices_ThenAccepts()
    {
        var element = PatientWith("""
        [
          {"url":"http://x/race","valueString":"A"},
          {"url":"http://x/extra","valueString":"trailing"}
        ]
        """);
        var metadata = ValueUrlSlicing(SlicingRules.OpenAtEnd, ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenOpenAtEndSlicing_WhenUnmatchedContentPrecedesMatchedSlice_ThenReportsUnmatchedError()
    {
        var element = PatientWith("""
        [
          {"url":"http://x/extra","valueString":"leading"},
          {"url":"http://x/race","valueString":"A"}
        ]
        """);
        var metadata = ValueUrlSlicing(SlicingRules.OpenAtEnd, ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("slicing-unmatched");
    }

    [Fact]
    public void GivenOpenAtEndEnumRule_WhenUnmatchedContentPrecedesMatchedSlice_ThenReportsUnmatchedError()
    {
        var element = PatientWith("""
        [
          {"url":"http://x/extra","valueString":"leading"},
          {"url":"http://x/race","valueString":"A"}
        ]
        """);
        var metadata = new SlicingMetadata(
            new[] { new DiscriminatorDefinition(DiscriminatorType.Value, "url") },
            SlicingRules.OpenAtEnd,
            ordered: false,
            new[] { ValueUrlSlice("race", 0, 1, "http://x/race") });
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("slicing-unmatched");
    }

    [Fact]
    public void GivenOrderedSlicing_WhenSlicesAppearOutOfOrder_ThenReportsOutOfOrderError()
    {
        var element = PatientWith("""
        [
          {"url":"http://x/birthsex","valueCode":"F"},
          {"url":"http://x/race","valueString":"A"}
        ]
        """);
        var metadata = new SlicingMetadata(
            new[] { new DiscriminatorDefinition(DiscriminatorType.Value, "url") },
            SlicingRules.Open,
            ordered: true,
            new[]
            {
                ValueUrlSlice("race", 0, 1, "http://x/race"),
                ValueUrlSlice("birthsex", 0, 1, "http://x/birthsex"),
            });
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "slicing-out-of-order" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenDiscriminatorPathThatThrows_WhenValidating_ThenDefersWithInformationNotError()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"A"},{"url":"http://x/race","valueString":"B"}]""");

        // %terminologies is a determinate value discriminator (so it is not statically deferred), but
        // evaluating it throws at runtime. A throw is indeterminate: the check must defer with an
        // Information issue, never raise a slicing Error (which would falsely reject a valid resource).
        var metadata = new SlicingMetadata(
            new[] { new DiscriminatorDefinition(DiscriminatorType.Value, "%terminologies") },
            SlicingRules.Closed,
            ordered: false,
            new[] { new SliceDefinition("race", 0, 1, new[] { new SliceDiscriminatorValue(DiscriminatorType.Value, "%terminologies", "http://x/race") }) });
        var check = new SlicingCheck("extension", metadata);

        check.IsDeferred.ShouldBeFalse();
        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotBeEmpty();
        result.Issues.ShouldAllBe(i => i.Severity == IssueSeverity.Information && i.Code == "slicing-deferred");
    }

    [Fact]
    public void GivenDiscriminatorPathWithAnOverflowingIntegerLiteral_WhenValidating_ThenDefersWithInformationNotError()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"A"},{"url":"http://x/race","valueString":"B"}]""");

        // An integer literal above int.MaxValue makes the FHIRPath parser itself throw OverflowException
        // while building the AST (FhirPathParseTreeGrammar's IntegerLiteral node uses int.Parse) - a
        // defect in the profile's discriminator path text, not the resource. Like the other malformed-path
        // cases (ArgumentException, FormatException), this must defer the slicing with an Information
        // issue, never raise a slicing Error that would falsely reject a valid resource.
        var metadata = new SlicingMetadata(
            new[] { new DiscriminatorDefinition(DiscriminatorType.Value, "99999999999999999999") },
            SlicingRules.Closed,
            ordered: false,
            new[] { new SliceDefinition("race", 0, 1, new[] { new SliceDiscriminatorValue(DiscriminatorType.Value, "99999999999999999999", "http://x/race") }) });
        var check = new SlicingCheck("extension", metadata);

        check.IsDeferred.ShouldBeFalse();
        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotBeEmpty();
        result.Issues.ShouldAllBe(i => i.Severity == IssueSeverity.Information && i.Code == "slicing-deferred");
    }

    [Fact]
    public void GivenTwoDistinctSlices_WhenEachWithinMax_ThenSucceeds()
    {
        var element = PatientWith("""
        [
          {"url":"http://x/race","valueString":"A"},
          {"url":"http://x/birthsex","valueCode":"F"}
        ]
        """);
        var metadata = ValueUrlSlicing(
            SlicingRules.Closed,
            ValueUrlSlice("race", 0, 1, "http://x/race"),
            ValueUrlSlice("birthsex", 0, 1, "http://x/birthsex"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, ValidationState.ForRoot(element));

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
