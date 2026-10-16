// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;
using static Ignixa.CliOnFhir.Tests.ViewFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>The verbs write to the process-wide console, so these tests must not run beside others that do.</summary>
[Collection("Console")]
public class SqlViewCommandTests
{
    [Fact]
    public async Task GivenAnInlineView_WhenRunOnTheCli_ThenTheEnvelopeIsPrintedAndExitCodeIsZero()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync("view", folder.Path, PatientView, "--no-cache");

        exitCode.ShouldBe(0);
        ((int)output["facts"]!["total"]!).ShouldBe(3);
        output["facts"]!["columns"]!.AsArray().Select(column => (string)column!["name"]!).ShouldBe(["id", "gender", "birth_date"]);
        ((string)output["facts"]!["rows"]![0]![1]!).ShouldBe("female");
        output["derived"].ShouldBeNull();
    }

    [Fact]
    public async Task GivenOptions_WhenRunOnTheCli_ThenTheyReachTheRun()
    {
        using TestFolder folder = ClinicWithConditions();
        const string filter = "code.coding.where(code = '44054006').exists()";

        (int exitCode, JsonNode output) = await RunAsync("view", folder.Path, ConditionView, "--where", filter, "--count", "0", "--distinct", "patient", "--compartment", "Patient/p1", "--no-cache");

        exitCode.ShouldBe(0);
        ((int)output["facts"]!["total"]!).ShouldBe(2);
        ((int)output["facts"]!["distinct"]!["values"]!).ShouldBe(1);
        ((string)output["facts"]!["compartment"]!).ShouldBe("Patient/p1");
    }

    [Fact]
    public async Task GivenVarOptionsOnTheCli_WhenRun_ThenEachIsAVariable()
    {
        using TestFolder folder = Clinic();
        const string view = """{"resourceType":"ViewDefinition","resource":"Patient","constant":[{"name":"cutoff","valueDate":"1980-01-01"}],"where":[{"path":"birthDate < %cutoff"}],"select":[{"column":[{"name":"id","path":"id"}]}]}""";

        (int exitCode, JsonNode output) = await RunAsync("view", folder.Path, view, "--var", "cutoff=1988-01-01", "--no-cache");

        exitCode.ShouldBe(0);
        ((int)output["facts"]!["total"]!).ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnInvalidView_WhenRunOnTheCli_ThenTheIssueIsPrintedAndExitCodeIsNonZero()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync("view", folder.Path, """{"resourceType":"ViewDefinition","resource":"Patient","select":[]}""", "--no-cache");

        exitCode.ShouldNotBe(0);
        output["facts"].ShouldBeNull();
        ((string)output["issues"]![0]!["code"]!).ShouldBe("invalid-view");
    }

    [Fact]
    public async Task GivenOutOnTheCli_WhenRun_ThenTheFileIsWrittenAndTheFactsDescribeIt()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "patients.ndjson");

        (int exitCode, JsonNode output) = await RunAsync("view", folder.Path, PatientView, "--out", path, "--no-cache");

        exitCode.ShouldBe(0);
        ((string)output["facts"]!["file"]!["path"]!).ShouldBe(path);
        ((int)output["facts"]!["file"]!["rows"]!).ShouldBe(3);
        (await File.ReadAllLinesAsync(path)).Length.ShouldBe(3);
    }

    [Fact]
    public async Task GivenOutInsideTheFolderOnTheCli_WhenRun_ThenExitCodeIsNonZeroAndNothingIsWritten()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync("view", folder.Path, PatientView, "--out", Path.Combine(folder.Path, "out.csv"), "--no-cache");

        exitCode.ShouldNotBe(0);
        ((string)output["issues"]![0]!["code"]!).ShouldBe("out-refused");
        File.Exists(Path.Combine(folder.Path, "out.csv")).ShouldBeFalse();
    }

    private static async Task<(int ExitCode, JsonNode Output)> RunAsync(params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(ViewCommand.Create());
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
