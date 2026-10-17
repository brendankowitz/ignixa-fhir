// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;
using static Ignixa.CliOnFhir.Tests.DraftFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>The draft verb on the command line (Task 12).</summary>
[Collection("Console")]
public class DraftCommandTests
{
    [Fact]
    public async Task GivenADraftOnTheCli_WhenRunWithAsOfAndOut_ThenTheEnvelopeIsPrintedAndTheFileWritten()
    {
        using TestFolder root = ClinicRoot();

        (int exitCode, string output, _) = await RunAsync("--as-of", "2024-05-02T00:00:00Z", "draft", Data(root), "ServiceRequest", "--input", MagnesiumOrder, "--out", Out(root), "--no-cache", "--offline");

        exitCode.ShouldBe(0);
        JsonNode envelope = JsonNode.Parse(output)!;
        JsonNode facts = envelope["facts"]!;
        ((string)facts["verdict"]!).ShouldNotBe("invalid");
        ((string)facts["resource"]!["authoredOn"]!).ShouldBe("2024-05-02T00:00:00Z");
        ((string)envelope["asOf"]!["source"]!).ShouldBe("session-option");
        string path = (string)facts["file"]!["path"]!;
        File.Exists(path).ShouldBeTrue();
        Path.GetDirectoryName(path).ShouldBe(Out(root));
        envelope["derived"].ShouldBeNull();
    }

    [Fact]
    public async Task GivenInvalidInputOnTheCli_WhenRun_ThenTheIssueIsPrintedAndExitCodeIsNonZero()
    {
        using TestFolder folder = Clinic();

        (int exitCode, string output, _) = await RunAsync("draft", folder.Path, "Observation", "--input", """{"patient":"p1","cod":{}}""", "--no-cache", "--offline");

        exitCode.ShouldNotBe(0);
        ((string)JsonNode.Parse(output)!["issues"]![0]!["code"]!).ShouldBe("draft-input-invalid");
    }

    [Fact]
    public async Task GivenNoInputOnTheCli_WhenParsed_ThenItIsAParseError()
    {
        using TestFolder folder = Clinic();

        (int exitCode, string output, string errors) = await RunAsync("draft", folder.Path, "Observation", "--no-cache");

        exitCode.ShouldNotBe(0);
        output.ShouldNotContain("\"facts\"");
        errors.ShouldContain("--input");
    }

    private static async Task<(int ExitCode, string Output, string Errors)> RunAsync(params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(DraftCommand.Create());
        AsOfOption.Register(root);
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Console.SetOut(output);
        Console.SetError(errors);
        try
        {
            int exitCode = await root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = errors });
            return (exitCode, output.ToString(), errors.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
