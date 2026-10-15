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
/// terminology_subsumes and terminology_navigate over the fixture hierarchy (nesting, the child property and subsumedBy).
/// New behavior (Task 7): 24779ff has no terminology store (verified: the file does not compile there).
/// </summary>
public class TerminologyHierarchyTests
{
    [Theory]
    [InlineData("disorder", "mi", "subsumes")]
    [InlineData("mi", "disorder", "subsumed-by")]
    [InlineData("heart", "heart", "equivalent")]
    [InlineData("disorder", "asthma", "subsumes")]
    [InlineData("lung", "asthma", "subsumes")]
    [InlineData("asthma", "lung", "subsumed-by")]
    [InlineData("mi", "asthma", "not-subsumed")]
    [InlineData("device", "disorder", "not-subsumed")]
    public async Task GivenACompleteHierarchy_WhenTwoCodesAreCompared_ThenTheRelationIsAnsweredFromIt(string codeA, string codeB, string expected)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermSubsumes.SubsumesAsync(OpenWithPackage(folder, store), Body, codeA, codeB, null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe(expected);
        ((string)result["facts"]!["version"]!).ShouldBe("1.0.0");
        ((string)result["facts"]!["hierarchyMeaning"]!).ShouldBe("is-a");
    }

    [Fact]
    public async Task GivenASystemThatIsNotLoaded_WhenCompared_ThenTheOutcomeIsSystemNotLoadedNeverNotSubsumed()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermSubsumes.SubsumesAsync(OpenWithPackage(folder, store), Snomed, "73211009", "46635009", null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("system-not-loaded");
        result["absent"]!.AsArray().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenACodeTheSystemDoesNotDefine_WhenCompared_ThenTheOutcomeNamesTheMissingCode()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermSubsumes.SubsumesAsync(OpenWithPackage(folder, store), Body, "disorder", "nope", null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("code-not-in-version");
        result["facts"]!["codesNotFound"]!.AsArray().Select(code => (string)code!).ShouldBe(["nope"]);
    }

    [Fact]
    public async Task GivenAFragment_WhenUnrelatedCodesAreCompared_ThenTheOutcomeIsUnknownNotNotSubsumed()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = OpenWithPackage(folder, store);

        JsonNode related = Json(await TermSubsumes.SubsumesAsync(session, Partial, "a", "b", null, CancellationToken.None));
        JsonNode unrelated = Json(await TermSubsumes.SubsumesAsync(session, Partial, "b", "c", null, CancellationToken.None));

        ((string)related["facts"]!["outcome"]!).ShouldBe("subsumes");
        ((string)unrelated["facts"]!["outcome"]!).ShouldBe("unknown");
        unrelated["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldContain("hierarchy-incomplete");
    }

    [Fact]
    public async Task GivenDepthTwo_WhenDescendantsAreNavigated_ThenEveryReachedConceptHasItsDistance()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermNavigate.NavigateAsync(OpenWithPackage(folder, store), Body, "disorder", "descendants", 2, null, 0, null, CancellationToken.None));

        result["facts"]!["concepts"]!.AsArray().Select(concept => ((string)concept!["code"]!, (int)concept["distance"]!))
            .ShouldBe([("heart", 1), ("old", 1), ("lung", 1), ("mi", 2), ("asthma", 2)]);
        result["facts"]!["concepts"]!.AsArray().Single(concept => (string)concept!["code"]! == "old")!["inactive"]!.GetValue<bool>().ShouldBeTrue();
        result["next"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenTheDefaultDepth_WhenDeeperLevelsExist_ThenOmittedAndNextSaySo()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermNavigate.NavigateAsync(OpenWithPackage(folder, store), Body, "disorder", null, null, null, 0, null, CancellationToken.None));

        ((int)result["facts"]!["total"]!).ShouldBe(3);
        ((string)result["omitted"]![0]!["why"]!).ShouldBe("depth");
        ((int)result["next"]![0]!["args"]!["depth"]!).ShouldBe(2);
    }

    [Fact]
    public async Task GivenAncestors_WhenNavigatedFromALeaf_ThenParentsComeNearestFirst()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermNavigate.NavigateAsync(OpenWithPackage(folder, store), Body, "asthma", "ancestors", 5, null, 0, null, CancellationToken.None));

        result["facts"]!["concepts"]!.AsArray().Select(concept => (string)concept!["code"]!).ShouldBe(["lung", "disorder"]);
    }

    [Fact]
    public async Task GivenOnePerPage_WhenNavigated_ThenNextCarriesTheNextOffset()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermNavigate.NavigateAsync(OpenWithPackage(folder, store), Body, "disorder", "descendants", 1, 1, 0, null, CancellationToken.None));

        ((int)result["facts"]!["returned"]!).ShouldBe(1);
        ((int)result["next"]![0]!["args"]!["offset"]!).ShouldBe(1);
        ((string)result["omitted"]![0]!["what"]!).ShouldBe("2 more concepts");
    }
}
