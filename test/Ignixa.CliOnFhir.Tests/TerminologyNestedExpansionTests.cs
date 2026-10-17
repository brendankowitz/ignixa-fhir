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
/// Nested ValueSet expansion (Task 7 fix round 2, F1): a ValueSet reached along many include paths is expanded once, cycles
/// are found before expanding, and the expansion honours cancellation. Regressions: each fails at 4073a28 (verified in a
/// worktree at that commit), where a 24-deep diamond takes minutes and a cancelled token is ignored.
/// </summary>
public class TerminologyNestedExpansionTests
{
    private const string CodeSystemUrl = "http://example.org/fhir/CodeSystem/diamond";
    private const string Base = "http://example.org/fhir/ValueSet/diamond-";
    private const int Depth = 24;

    // Far above the linear expansion (milliseconds) and far below the exponential one (2^24 nested expansions).
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    // diamond-0 .. diamond-(Depth-1) each include the next one twice; the last includes the code system. With closeCycle,
    // the last also includes diamond-0, which puts every ValueSet in one cycle.
    private static string[] Diamond(bool closeCycle) =>
    [
        CodeSystem(CodeSystemUrl, "1", "", "a:Alpha", "b:Beta"),
        .. Enumerable.Range(0, Depth).Select(level => level == Depth - 1
            ? $$$"""{"resourceType":"ValueSet","url":"{{{Base}}}{{{level}}}","compose":{"include":[{"system":"{{{CodeSystemUrl}}}"}{{{(closeCycle ? $$""",{"valueSet":["{{Base}}0"]}""" : "")}}}]}}"""
            : $$$"""{"resourceType":"ValueSet","url":"{{{Base}}}{{{level}}}","compose":{"include":[{"valueSet":["{{{Base}}}{{{level + 1}}}"]},{"valueSet":["{{{Base}}}{{{level + 1}}}"]}]}}"""),
    ];

#pragma warning disable CA2000 // The registry client owns the stub handler for the session's lifetime.
    private static FolderSession Session(TestFolder folder, TestFolder store, bool closeCycle) =>
        TerminologyFixture.Open(folder, Online(Registry([("pkg.diamond", "1.0.0", Archive(PackageFiles("pkg.diamond", "1.0.0", "4.0.1", Diamond(closeCycle))), null)]), store, "pkg.diamond@1.0.0"));
#pragma warning restore CA2000

    private static IEnumerable<string> Codes(JsonNode result) => result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!);

    private static IEnumerable<string> Expanded(JsonNode result) => result["facts"]!["codes"]!.AsArray().Select(code => (string)code!["code"]!);

    private static async Task<JsonNode> ExpandWithinBoundAsync(FolderSession session, string valueSet, CancellationToken cancellationToken)
    {
        _ = await TermList.ListAsync(session, null, null, null, null, 0, false, cancellationToken);
        return Json(await Task.Run(() => TermExpand.ExpandAsync(session, valueSet, null, null, 0, cancellationToken), cancellationToken).WaitAsync(Bound, cancellationToken));
    }

    [Fact]
    public async Task GivenADeepDiamondOfNestedValueSets_WhenTheTopIsExpanded_ThenEachNestedValueSetIsExpandedOnceWithinTheBound()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = await ExpandWithinBoundAsync(Session(folder, store, closeCycle: false), Base + "0", CancellationToken.None);

        ((bool)result["facts"]!["complete"]!).ShouldBeTrue();
        Expanded(result).ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task GivenADeepDiamondWhoseBottomIncludesTheTop_WhenTheTopIsExpanded_ThenTheCycleIsReportedWithinTheBound()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = await ExpandWithinBoundAsync(Session(folder, store, closeCycle: true), Base + "0", CancellationToken.None);

        ((bool)result["facts"]!["complete"]!).ShouldBeFalse();
        Codes(result).ShouldContain("value-set-cycle");
    }

    [Fact]
    public async Task GivenACancelledRequest_WhenAValueSetIsExpanded_ThenItIsCancelledAndTheNextRequestStillExpandsIt()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, closeCycle: false);
        _ = await TermList.ListAsync(session, null, null, null, null, 0, false, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => TermExpand.ExpandAsync(session, Base + "0", null, null, 0, cancelled.Token));
        JsonNode result = await ExpandWithinBoundAsync(session, Base + "0", CancellationToken.None);

        Expanded(result).ShouldBe(["a", "b"]);
    }
}
