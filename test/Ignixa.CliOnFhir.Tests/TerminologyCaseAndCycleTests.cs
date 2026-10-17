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
/// Case-insensitive code systems, abstract concepts, non-is-a hierarchies and ValueSet include cycles (Task 7 review items
/// 2, 6 and 9a/9b/9i). Regressions: each fails at 96d6150 (verified in a worktree at that commit).
/// </summary>
public class TerminologyCaseAndCycleTests
{
    private const string Insensitive = "http://example.org/fhir/CodeSystem/insensitive";
    private const string Unstated = "http://example.org/fhir/CodeSystem/unstated";
    private const string Grouped = "http://example.org/fhir/CodeSystem/grouped";
    private const string Sensitive = "http://example.org/fhir/CodeSystem/sensitive";
    private const string Base = "http://example.org/fhir/ValueSet/";

    private static readonly string[] Resources =
    [
        CodeSystem(Insensitive, "1", ",\"caseSensitive\":false", "Abc:Alpha"),
        CodeSystem(Unstated, "1", "", "Xyz:Ex"),
        CodeSystem(Sensitive, "1", ",\"caseSensitive\":true", "Qrs"),
        $$"""{"resourceType":"CodeSystem","url":"{{Grouped}}","version":"1","content":"complete","hierarchyMeaning":"grouped-by","concept":[{"code":"group","property":[{"code":"notSelectable","valueBoolean":true}],"concept":[{"code":"member"}]}]}""",
        $$$"""{"resourceType":"ValueSet","url":"{{{Base}}}insensitive","compose":{"include":[{"system":"{{{Insensitive}}}"}]}}""",
        $$$"""{"resourceType":"ValueSet","url":"{{{Base}}}unstated","compose":{"include":[{"system":"{{{Unstated}}}"}]}}""",
        $$$"""{"resourceType":"ValueSet","url":"{{{Base}}}grouped","compose":{"include":[{"system":"{{{Grouped}}}"}]}}""",
        $$$"""{"resourceType":"ValueSet","url":"{{{Base}}}cycle-a","compose":{"include":[{"valueSet":["{{{Base}}}cycle-b"]},{"system":"{{{Insensitive}}}","concept":[{"code":"Abc"}]}]}}""",
        $$$"""{"resourceType":"ValueSet","url":"{{{Base}}}cycle-b","compose":{"include":[{"valueSet":["{{{Base}}}cycle-a"]},{"system":"{{{Unstated}}}","concept":[{"code":"Xyz"}]}]}}""",
    ];

#pragma warning disable CA2000 // The registry client owns the stub handler for the session's lifetime.
    private static FolderSession Session(TestFolder folder, TestFolder store) =>
        TerminologyFixture.Open(folder, Online(Registry([("pkg.case", "1.0.0", Archive(PackageFiles("pkg.case", "1.0.0", "4.0.1", Resources)), null)]), store, "pkg.case@1.0.0"));
#pragma warning restore CA2000

    private static IEnumerable<string> Codes(JsonNode result) => result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!);

    [Fact]
    public async Task GivenMutuallyIncludingValueSetsExpandedConcurrently_WhenRepeatedOnFreshStores_ThenEachReportsTheCycleWithoutCrashing()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        _ = await TermList.ListAsync(Session(folder, store), null, null, null, null, 0, false, CancellationToken.None);

        for (int round = 0; round < 40; round++)
        {
            FolderSession session = Session(folder, store);
            _ = await TermList.ListAsync(session, null, null, null, null, 0, false, CancellationToken.None);
            JsonNode[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
                Json(await TermExpand.ExpandAsync(session, Base + (index % 2 == 0 ? "cycle-a" : "cycle-b"), null, null, 0, CancellationToken.None)))));

            foreach (JsonNode result in results)
            {
                Codes(result).ShouldContain("value-set-cycle");
                ((bool)result["facts"]!["complete"]!).ShouldBeFalse();
            }
        }
    }

    [Theory]
    [InlineData("abc", "valid")]
    [InlineData("ABC", "valid")]
    public async Task GivenACaseInsensitiveCodeSystem_WhenACodeInAnotherCaseIsValidatedAgainstAValueSet_ThenItIsAMember(string code, string expected)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermValidate.ValidateAsync(Session(folder, store), code, Insensitive, null, Base + "insensitive", null, CancellationToken.None));

        ((string)result["facts"]!["result"]!).ShouldBe(expected);
        ((string)result["facts"]!["code"]!).ShouldBe("Abc");
        Codes(result).ShouldNotContain("case-insensitive-match");
    }

    [Fact]
    public async Task GivenACodeSystemThatDoesNotStateCaseSensitivity_WhenACodeInAnotherCaseIsUsed_ThenItMatchesWithANote()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store);

        JsonNode lookup = Json(await TermLookup.LookupAsync(session, Unstated, "xyz", null, CancellationToken.None));
        JsonNode member = Json(await TermValidate.ValidateAsync(session, "XYZ", Unstated, null, Base + "unstated", null, CancellationToken.None));

        ((string)lookup["facts"]!["outcome"]!).ShouldBe("found");
        ((string)lookup["facts"]!["code"]!).ShouldBe("Xyz");
        Codes(lookup).ShouldContain("case-insensitive-match");
        ((string)member["facts"]!["result"]!).ShouldBe("valid");
        Codes(member).ShouldContain("case-insensitive-match");
    }

    // Guard: a case-sensitive system still refuses another case.
    [Fact]
    public async Task GivenACaseSensitiveCodeSystem_WhenACodeInAnotherCaseIsLookedUp_ThenItIsNotInTheVersion()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermLookup.LookupAsync(Session(folder, store), Sensitive, "qrs", null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("code-not-in-version");
    }

    [Fact]
    public async Task GivenAGroupedByHierarchy_WhenAParentAndChildAreCompared_ThenSubsumptionIsUnknown()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermSubsumes.SubsumesAsync(Session(folder, store), Grouped, "group", "member", null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("unknown");
        Codes(result).ShouldContain("hierarchy-not-is-a");
    }

    [Fact]
    public async Task GivenANotSelectableConcept_WhenExpanded_ThenItIsFlaggedAbstract()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermExpand.ExpandAsync(Session(folder, store), Base + "grouped", null, null, 0, CancellationToken.None));

        result["facts"]!["codes"]!.AsArray().Select(code => ((string)code!["code"]!, (bool)code["abstract"]!)).ShouldBe([("group", true), ("member", false)]);
    }
}
