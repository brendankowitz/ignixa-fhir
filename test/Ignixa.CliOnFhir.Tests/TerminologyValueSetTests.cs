// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Tools;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.TerminologyFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// terminology_expand and terminology_validate_code over the fixture ValueSets.
/// New behavior (Task 7): 24779ff has no terminology store (verified: the file does not compile there).
/// </summary>
public class TerminologyValueSetTests
{
    private static async Task<JsonNode> Expand(string id, string? filter = null, int? count = null)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        return Json(await TermExpand.ExpandAsync(OpenWithPackage(folder, store), ValueSetBase + id, filter, count, 0, CancellationToken.None));
    }

    private static IEnumerable<string> Codes(JsonNode result) => result["facts"]!["codes"]!.AsArray().Select(code => (string)code!["code"]!);

    [Fact]
    public async Task GivenAnIsAFilter_WhenExpanded_ThenTheConceptAndItsDescendantsAreTheCompleteExpansion()
    {
        JsonNode result = await Expand("heart");

        Codes(result).ShouldBe(["heart", "mi"]);
        ((bool)result["facts"]!["complete"]!).ShouldBeTrue();
        ((string)result["facts"]!["source"]!["file"]!).ShouldBe("package/ValueSet-heart.json");
        result["evidence"]!.AsArray().Select(item => (string)item!["reference"]!).ShouldBe(["ValueSet/heart", "CodeSystem/body"]);
    }

    [Fact]
    public async Task GivenAnExcludeRule_WhenExpanded_ThenTheExcludedCodeIsLeftOut()
    {
        JsonNode result = await Expand("current-disorders");

        Codes(result).ShouldBe(["disorder", "heart", "mi", "lung", "asthma"]);
    }

    [Fact]
    public async Task GivenDescendentOf_WhenExpanded_ThenTheAnchorIsNotIncluded()
    {
        JsonNode result = await Expand("below-disorder");

        Codes(result).ShouldBe(["heart", "mi", "old", "lung", "asthma"]);
    }

    [Fact]
    public async Task GivenNestedValueSets_WhenExpanded_ThenTheirIntersectionIsReturned()
    {
        JsonNode result = await Expand("nested");

        Codes(result).ShouldBe(["heart", "mi"]);
        ((bool)result["facts"]!["complete"]!).ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAFilterTheToolCannotEvaluate_WhenExpanded_ThenTheExpansionIsIncompleteWithAnIssuePerRule()
    {
        JsonNode result = await Expand("regex");

        ((bool)result["facts"]!["complete"]!).ShouldBeFalse();
        result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldBe(["expansion-incomplete", "filter-not-supported"]);
        result["absent"]!.AsArray().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenAFilterOverASystemThatIsNotLoaded_WhenExpanded_ThenTheExpansionIsIncomplete()
    {
        JsonNode result = await Expand("snomed-filter");

        ((bool)result["facts"]!["complete"]!).ShouldBeFalse();
        result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldContain("system-not-loaded");
    }

    [Fact]
    public async Task GivenListedCodesOfASystemThatIsNotLoaded_WhenExpanded_ThenTheyAreIncludedUncheckedWithAnInformationIssue()
    {
        JsonNode result = await Expand("snomed-listed");

        Codes(result).ShouldBe(["22298006"]);
        ((bool)result["facts"]!["complete"]!).ShouldBeTrue();
        result["facts"]!["codes"]![0]!["inactive"].ShouldBeNull();
        ((string)result["issues"]![0]!["code"]!).ShouldBe("codes-not-checked");
    }

    [Fact]
    public async Task GivenAWholeFragmentSystem_WhenExpanded_ThenTheExpansionIsIncomplete()
    {
        JsonNode result = await Expand("partial-all");

        Codes(result).ShouldBe(["a", "b", "c"]);
        ((bool)result["facts"]!["complete"]!).ShouldBeFalse();
        result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldContain("content-not-complete");
    }

    [Fact]
    public async Task GivenATextFilterAndPaging_WhenExpanded_ThenOnlyMatchingCodesAreCountedAndNextHasTheOffset()
    {
        JsonNode result = await Expand("current-disorders", filter: "disorder", count: 1);

        ((int)result["facts"]!["total"]!).ShouldBe(3);
        Codes(result).ShouldBe(["disorder"]);
        ((int)result["next"]![0]!["args"]!["offset"]!).ShouldBe(1);
        ((string)result["next"]![0]!["args"]!["filter"]!).ShouldBe("disorder");
    }

    [Fact]
    public async Task GivenAValueSetThatIsNotLoaded_WhenExpanded_ThenTheOutcomeSaysSo()
    {
        JsonNode result = await Expand("none");

        ((string)result["facts"]!["outcome"]!).ShouldBe("value-set-not-loaded");
        ((string)result["absent"]![0]!["what"]!).ShouldBe($"ValueSet {ValueSetBase}none");
    }

    [Theory]
    [InlineData("mi", null, "valid", "code-in-value-set")]
    [InlineData("asthma", null, "invalid", "not-in-value-set")]
    [InlineData("mi", Body, "valid", "code-in-value-set")]
    public async Task GivenACompleteValueSet_WhenACodeIsValidated_ThenMembershipDecidesTheResult(string code, string? system, string result, string reason)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode answer = Json(await TermValidate.ValidateAsync(OpenWithPackage(folder, store), code, system, null, ValueSetBase + "heart", null, CancellationToken.None));

        ((string)answer["facts"]!["result"]!).ShouldBe(result);
        ((string)answer["facts"]!["reason"]!).ShouldBe(reason);
    }

    [Fact]
    public async Task GivenAnIncompleteExpansion_WhenACodeIsNotInIt_ThenTheResultIsUnknownNotInvalid()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode answer = Json(await TermValidate.ValidateAsync(OpenWithPackage(folder, store), "22298006", Snomed, null, ValueSetBase + "snomed-filter", null, CancellationToken.None));

        ((string)answer["facts"]!["result"]!).ShouldBe("unknown");
        ((string)answer["facts"]!["reason"]!).ShouldBe("expansion-incomplete");
    }

    [Fact]
    public async Task GivenACodeAndAWrongDisplay_WhenValidatedAgainstTheSystem_ThenItIsValidWithADisplayMismatch()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode answer = Json(await TermValidate.ValidateAsync(OpenWithPackage(folder, store), "heart", Body, "Kidney", null, null, CancellationToken.None));

        ((string)answer["facts"]!["result"]!).ShouldBe("valid");
        ((bool)answer["facts"]!["displayMatches"]!).ShouldBeFalse();
        ((string)answer["facts"]!["display"]!).ShouldBe("Heart disorder");
        answer["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldBe(["display-mismatch"]);
    }

    [Fact]
    public async Task GivenADesignation_WhenGivenAsTheDisplay_ThenItMatches()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode answer = Json(await TermValidate.ValidateAsync(OpenWithPackage(folder, store), "heart", Body, "herzkrankheit", null, null, CancellationToken.None));

        ((bool)answer["facts"]!["displayMatches"]!).ShouldBeTrue();
    }

    [Theory]
    [InlineData(Body, "nope", "invalid", "code-not-in-version")]
    [InlineData(Body, "old", "valid", "code-in-system")]
    [InlineData(Partial, "z", "unknown", "code-not-in-loaded-content")]
    [InlineData(Snomed, "22298006", "unknown", "system-not-loaded")]
    public async Task GivenOnlyASystem_WhenACodeIsValidated_ThenOnlyCompleteContentGivesInvalid(string system, string code, string result, string reason)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode answer = Json(await TermValidate.ValidateAsync(OpenWithPackage(folder, store), code, system, null, null, null, CancellationToken.None));

        ((string)answer["facts"]!["result"]!).ShouldBe(result);
        ((string)answer["facts"]!["reason"]!).ShouldBe(reason);
    }

    [Fact]
    public async Task GivenAnInactiveCode_WhenValidated_ThenItIsValidAndFlaggedInactive()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode answer = Json(await TermValidate.ValidateAsync(OpenWithPackage(folder, store), "old", Body, null, null, null, CancellationToken.None));

        ((bool)answer["facts"]!["inactive"]!).ShouldBeTrue();
        answer["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldContain("inactive-concept");
    }

    [Fact]
    public async Task GivenNeitherSystemNorValueSet_WhenValidated_ThenTheCallFails()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode answer = Json(await TermValidate.ValidateAsync(OpenWithPackage(folder, store), "heart", null, null, null, null, CancellationToken.None));

        answer["facts"].ShouldBeNull();
        ((string)answer["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
    }
}
