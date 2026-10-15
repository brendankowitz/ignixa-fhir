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
/// terminology_search and terminology_translate over the fixture package.
/// New behavior (Task 7): 24779ff has no terminology store (verified: the file does not compile there).
/// </summary>
public class TerminologySearchTranslateTests
{
    [Fact]
    public async Task GivenText_WhenSearched_ThenExactComesBeforePrefixBeforeContains()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermSearch.SearchAsync(OpenWithPackage(folder, store), "disorder", Body, null, null, 0, CancellationToken.None));

        result["facts"]!["matches"]!.AsArray().Select(match => ((string)match!["code"]!, (string)match["matchKind"]!))
            .ShouldBe([("disorder", "exact"), ("lung", "contains"), ("heart", "contains"), ("old", "contains")]);
    }

    [Fact]
    public async Task GivenADesignationOnlyMatch_WhenSearchedWithoutAccentsOrCase_ThenTheDesignationIsNamed()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermSearch.SearchAsync(OpenWithPackage(folder, store), "HERZ", null, null, null, 0, CancellationToken.None));

        JsonNode match = result["facts"]!["matches"]!.AsArray().ShouldHaveSingleItem()!;
        ((string)match["code"]!).ShouldBe("heart");
        ((string)match["matchedOn"]!).ShouldBe("designation");
        ((string)match["matchKind"]!).ShouldBe("prefix");
        ((string)match["matchedText"]!).ShouldBe("Herzkrankheit");
    }

    [Fact]
    public async Task GivenAValueSet_WhenSearched_ThenOnlyItsCodesMatch()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermSearch.SearchAsync(OpenWithPackage(folder, store), "disorder", null, ValueSetBase + "heart", null, 0, CancellationToken.None));

        result["facts"]!["matches"]!.AsArray().Select(match => (string)match!["code"]!).ShouldBe(["heart"]);
        ((bool)result["facts"]!["valueSetComplete"]!).ShouldBeTrue();
    }

    [Fact]
    public async Task GivenOnePerPage_WhenSearched_ThenNextCarriesTheOffsetAndTheCallerText()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermSearch.SearchAsync(OpenWithPackage(folder, store), "disorder", Body, null, 1, 0, CancellationToken.None));

        ((int)result["facts"]!["total"]!).ShouldBe(4);
        ((int)result["next"]![0]!["args"]!["offset"]!).ShouldBe(1);
        ((string)result["next"]![0]!["args"]!["text"]!).ShouldBe("disorder");
    }

    [Fact]
    public async Task GivenTextNothingMatches_WhenSearched_ThenAbsentSaysWhatWasSearched()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermSearch.SearchAsync(OpenWithPackage(folder, store), "zebra", null, null, null, 0, CancellationToken.None));

        ((int)result["facts"]!["total"]!).ShouldBe(0);
        ((string)result["absent"]![0]!["checked"]!).ShouldContain("concepts in 2 code systems");
    }

    [Fact]
    public async Task GivenAMappedCode_WhenTranslated_ThenTheTargetComesWithItsEquivalenceAndMap()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermTranslate.TranslateAsync(OpenWithPackage(folder, store), Body, "mi", null, null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("mapped");
        JsonNode translation = result["facts"]!["translations"]!.AsArray().ShouldHaveSingleItem()!;
        ((string)translation["code"]!).ShouldBe("I21");
        ((string)translation["equivalence"]!).ShouldBe("equivalent");
        ((string)translation["targetSystem"]!).ShouldBe(Icd);
        ((string)translation["conceptMap"]!).ShouldBe(ConceptMapUrl);
        ((string)translation["via"]!).ShouldBe("element");
    }

    [Fact]
    public async Task GivenAnUnmatchedTarget_WhenTranslated_ThenTheOutcomeIsNotMapped()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermTranslate.TranslateAsync(OpenWithPackage(folder, store), Body, "asthma", null, null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("not-mapped");
        ((string)result["facts"]!["translations"]![0]!["equivalence"]!).ShouldBe("unmatched");
        result["absent"]!.AsArray().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenACodeTheGroupDoesNotList_WhenTranslated_ThenTheUnmappedFixedRuleAnswers()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermTranslate.TranslateAsync(OpenWithPackage(folder, store), Body, "device", null, null, CancellationToken.None));

        ((string)result["facts"]!["translations"]![0]!["code"]!).ShouldBe("R69");
        ((string)result["facts"]!["translations"]![0]!["via"]!).ShouldBe("unmapped-fixed");
    }

    [Theory]
    [InlineData(Snomed, null, null, "no-map-for-system")]
    [InlineData(Body, "http://loinc.org", null, "no-map-for-system")]
    [InlineData(Body, null, "http://example.org/fhir/ConceptMap/none", "concept-map-not-loaded")]
    public async Task GivenNoApplicableMap_WhenTranslated_ThenTheOutcomeSaysWhyAndAbsentWhatWasChecked(string system, string? target, string? conceptMap, string outcome)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermTranslate.TranslateAsync(OpenWithPackage(folder, store), system, "mi", target, conceptMap, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe(outcome);
        result["absent"]!.AsArray().ShouldHaveSingleItem();
    }

    // Regression (Task 7 review item 4): at 96d6150 a disjoint or not-related-to target counted as mapped.
    [Theory]
    [InlineData("\"equivalence\":\"disjoint\"")]
    [InlineData("\"relationship\":\"not-related-to\"")]
    public async Task GivenOnlyAnUnrelatedTarget_WhenTranslatedLocally_ThenTheOutcomeIsNotMapped(string relation)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        string map = $$$"""{"resourceType":"ConceptMap","url":"http://example.org/map","group":[{"source":"{{{Body}}}","target":"{{{Icd}}}","element":[{"code":"mi","target":[{"code":"Z00",{{{relation}}}}]}]}]}""";
#pragma warning disable CA2000 // The registry client owns the stub handler for the session's lifetime.
        FolderSession session = Open(folder, Online(Registry([("pkg.map", "1.0.0", Archive(PackageFiles("pkg.map", "1.0.0", "4.0.1", map)), null)]), store, "pkg.map@1.0.0"));
#pragma warning restore CA2000

        JsonNode result = Json(await TermTranslate.TranslateAsync(session, Body, "mi", null, null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("not-mapped");
        ((string)result["facts"]!["translations"]![0]!["code"]!).ShouldBe("Z00");
    }
}
