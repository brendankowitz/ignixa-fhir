// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Ignixa.CliOnFhir.Terminology.Tools;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.TerminologyFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The <c>term</c> verbs print the same envelope as the MCP tools. They write to the process-wide console, so they share
/// the console collection. New behavior (Task 7): 24779ff has no <c>term</c> verb (verified: the file does not compile there).
/// </summary>
[Collection("Console")]
public class TerminologyCommandTests
{
    [Fact]
    public async Task GivenAStoredPackage_WhenTermVerbsRunOffline_ThenTheyAnswerLikeTheMcpToolsAndExitZero()
    {
        using var folder = new TestFolder();
        using var cache = new TestFolder();
        using var noFhirCache = new TestFolder();
        _ = await TermList.ListAsync(OpenWithPackage(folder, cache), null, null, null, null, 0, false, CancellationToken.None);
        string[] common = ["--cache-dir", cache.Path, "--fhir-package-cache", noFhirCache.Path, "--offline", "--terminology", $"{PackageId}@{PackageVersion}"];

        (int lookupExit, JsonNode lookup) = await RunAsync(["term", "lookup", folder.Path, Body, "mi", .. common]);
        (int subsumesExit, JsonNode subsumes) = await RunAsync(["term", "subsumes", folder.Path, Body, "disorder", "mi", .. common]);
        (int treeExit, JsonNode tree) = await RunAsync(["term", "tree", folder.Path, Body, "disorder", "--depth", "2", .. common]);
        (int expandExit, JsonNode expand) = await RunAsync(["term", "expand", folder.Path, ValueSetBase + "heart", .. common]);

        (lookupExit, subsumesExit, treeExit, expandExit).ShouldBe((0, 0, 0, 0));
        ((string)lookup["facts"]!["display"]!).ShouldBe("Myocardial infarction");
        ((string)lookup["facts"]!["source"]!["package"]!).ShouldBe($"{PackageId}@{PackageVersion}");
        ((string)subsumes["facts"]!["outcome"]!).ShouldBe("subsumes");
        ((int)tree["facts"]!["total"]!).ShouldBe(5);
        ((int)expand["facts"]!["total"]!).ShouldBe(2);
    }

    [Fact]
    public async Task GivenACallThatFails_WhenRunOnTheCli_ThenTheIssueIsPrintedAndTheExitCodeIsNonZero()
    {
        using var folder = new TestFolder();
        using var cache = new TestFolder();
        using var noFhirCache = new TestFolder();

        (int exitCode, JsonNode output) = await RunAsync(["term", "lookup", folder.Path, Body, "mi", "--cache-dir", cache.Path, "--fhir-package-cache", noFhirCache.Path, "--offline"]);

        exitCode.ShouldNotBe(0);
        output["facts"].ShouldBeNull();
        output["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldBe(["terminology-not-loaded", "package-unavailable-offline"]);
    }

    [Fact]
    public async Task GivenAnInvalidTerminologyOption_WhenParsed_ThenTheCommandIsRejectedBeforeRunning()
    {
        using var folder = new TestFolder();

        var root = new RootCommand();
        root.Subcommands.Add(TermCommand.Create());
        ParseResult parse = root.Parse(["term", "list", folder.Path, "--terminology", "hl7.fhir.r4.core@latest"]);

        parse.Errors.ShouldHaveSingleItem().Message.ShouldContain("--terminology hl7.fhir.r4.core@latest");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GivenWithTerminology_WhenInspectRuns_ThenTheTerminologySectionIsComputed()
    {
        using var folder = new TestFolder();
        using var cache = new TestFolder();
        using var noFhirCache = new TestFolder();
        _ = await TermList.ListAsync(OpenWithPackage(folder, cache), null, null, null, null, 0, false, CancellationToken.None);

        (int exitCode, JsonNode output) = await RunAsync(["inspect", folder.Path, "--with-terminology", "--cache-dir", cache.Path, "--fhir-package-cache", noFhirCache.Path, "--offline", "--terminology", $"{PackageId}@{PackageVersion}"]);

        exitCode.ShouldBe(0);
        ((string)output["facts"]!["terminology"]!["status"]!).ShouldBe("loaded");
    }

    private static async Task<(int ExitCode, JsonNode Output)> RunAsync(string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(TermCommand.Create());
        root.Subcommands.Add(InspectCommand.Create());
        TextWriter original = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            int exitCode = await root.Parse(args).InvokeAsync();
            return (exitCode, JsonNode.Parse(captured.ToString())!);
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
