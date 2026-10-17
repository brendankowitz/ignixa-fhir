// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;
using static Ignixa.CliOnFhir.Tests.MapFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>The verb writes to the process-wide console, so these tests must not run beside others that do.</summary>
[Collection("Console")]
public class MapCommandTests
{
    [Fact]
    public async Task GivenAMapFile_WhenRunOnTheCli_ThenThePreviewIsPrintedAndExitCodeIsZero()
    {
        using TestFolder root = Root().Write("patient-basic.fml", PatientToBasic);

        (int exitCode, JsonNode output) = await RunCliAsync("map", Data(root), "--map", Path.Combine(root.Path, "patient-basic.fml"), "--no-cache");

        exitCode.ShouldBe(0);
        ((int)output["facts"]!["returned"]!).ShouldBe(2);
        ((string)output["facts"]!["resources"]![0]!["created"]!).ShouldBe("1975-03-04");
        ((string)output["facts"]!["map"]!["source"]!).ShouldBe("file");
    }

    [Fact]
    public async Task GivenImportsAnOutputDirectoryAndAScope_WhenRunOnTheCli_ThenTheyReachTheRun()
    {
        using TestFolder root = Root().Write("main.fml", Main).Write("helpers.fml", Helpers);

        (int exitCode, JsonNode output) = await RunCliAsync("map", Data(root), "--map", Path.Combine(root.Path, "main.fml"), "--import", Path.Combine(root.Path, "helpers.fml"), "--out", Out(root), "--reference", "Patient/p2", "--no-cache");

        exitCode.ShouldBe(0);
        ((int)output["facts"]!["transformed"]!).ShouldBe(1);
        ((string)output["facts"]!["scope"]!["kind"]!).ShouldBe("references");
        (await File.ReadAllTextAsync(Path.Combine(Out(root), "Basic.ndjson"))).ShouldBe("""{"resourceType":"Basic","id":"p2"}""" + "\n");
    }

    [Fact]
    public async Task GivenAMapThatDoesNotParse_WhenRunOnTheCli_ThenTheIssueIsPrintedAndExitCodeIsNonZero()
    {
        using TestFolder root = Root();

        (int exitCode, JsonNode output) = await RunCliAsync("map", Data(root), "--map", "map \"http://e/x\" = \"X\"\ngroup X(source src : Patient, target tgt : Basic) {\n  -> ;\n}\n", "--no-cache");

        exitCode.ShouldNotBe(0);
        ((string)output["issues"]![0]!["code"]!).ShouldBe("map-invalid");
    }

    private static async Task<(int ExitCode, JsonNode Output)> RunCliAsync(params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(MapCommand.Create());
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
