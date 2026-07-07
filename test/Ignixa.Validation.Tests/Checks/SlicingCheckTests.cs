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
/// Unit tests for <see cref="SlicingCheck"/>: slice assignment by discriminator, per-slice
/// cardinality accounting, closed / openAtEnd rule enforcement, each supported discriminator type,
/// the Full-tier depth gate, and the profile-discriminator deferral.
/// </summary>
public sealed class SlicingCheckTests
{
    private static readonly ValidationSettings FullDepth = new() { Depth = ValidationDepth.Full };

    private static IElement PatientWith(string extensionsJson)
    {
        var json = JsonNode.Parse($$"""{"resourceType":"Patient","extension":{{extensionsJson}}}""");
        return JsonNodeSourceNode.Create(json).ToElement(TestSchemaProvider.GetR4Schema());
    }

    private static SlicingMetadata ValueUrlSlicing(string rules, params SliceDefinition[] slices)
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
        var metadata = ValueUrlSlicing("closed", ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, new ValidationState());

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTwoExtensionsForSingleCardinalitySlice_WhenValidating_ThenReportsSliceCardinalityError()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"A"},{"url":"http://x/race","valueString":"B"}]""");
        var metadata = ValueUrlSlicing("open", ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, new ValidationState());

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
        var metadata = ValueUrlSlicing("closed", ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, new ValidationState());

        result.IsValid.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("slicing-unmatched");
    }

    [Fact]
    public void GivenUnknownUrlUnderOpenSlicing_WhenValidating_ThenAcceptsAsDefaultBucket()
    {
        var element = PatientWith("""[{"url":"http://x/unknown","valueString":"?"}]""");
        var metadata = ValueUrlSlicing("open", ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, new ValidationState());

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenMandatorySliceAbsent_WhenValidating_ThenReportsMinCardinalityError()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"A"}]""");
        var metadata = ValueUrlSlicing(
            "open",
            ValueUrlSlice("race", 0, 1, "http://x/race"),
            ValueUrlSlice("birthsex", 1, 1, "http://x/birthsex"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, new ValidationState());

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
            "closed",
            ordered: false,
            new[] { new SliceDefinition("hasValueString", 1, 1, new[] { new SliceDiscriminatorValue(DiscriminatorType.Exists, "valueString", null) }) });
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, new ValidationState());

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenSpecDepth_WhenValidating_ThenSkippedRegardlessOfViolations()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"A"},{"url":"http://x/race","valueString":"B"}]""");
        var metadata = ValueUrlSlicing("closed", ValueUrlSlice("race", 0, 1, "http://x/race"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec }, new ValidationState());

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenProfileDiscriminator_WhenValidating_ThenDeferredWithInformationOnly()
    {
        var element = PatientWith("""[{"url":"http://x/race","valueString":"A"},{"url":"http://x/race","valueString":"B"}]""");
        var metadata = new SlicingMetadata(
            new[] { new DiscriminatorDefinition(DiscriminatorType.Profile, "$this") },
            "closed",
            ordered: false,
            new[] { new SliceDefinition("race", 0, 1, new[] { new SliceDiscriminatorValue(DiscriminatorType.Profile, "$this", "http://x/race") }) });
        var check = new SlicingCheck("extension", metadata);

        check.IsDeferred.ShouldBeTrue();
        var result = check.Validate(element, FullDepth, new ValidationState());

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldAllBe(i => i.Severity == IssueSeverity.Information);
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
            "closed",
            ValueUrlSlice("race", 0, 1, "http://x/race"),
            ValueUrlSlice("birthsex", 0, 1, "http://x/birthsex"));
        var check = new SlicingCheck("extension", metadata);

        var result = check.Validate(element, FullDepth, new ValidationState());

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
