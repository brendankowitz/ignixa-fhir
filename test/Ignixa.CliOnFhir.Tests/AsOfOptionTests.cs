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

/// <summary>The one recursive root --as-of option (Task 12): read by view and draft, refused by every other verb.</summary>
[Collection("Console")]
public class AsOfOptionTests
{
    private const string AsOfView = """
        {"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[{"name":"id","path":"id"},{"name":"as_of","path":"%asOf.toString()"}]}]}
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenAsOfBeforeOrAfterTheVerb_WhenViewRuns_ThenItIsTheSessionOption(bool before)
    {
        using TestFolder folder = Clinic();
        string[] view = ["view", folder.Path, AsOfView, "--no-cache"];
        string[] asOf = ["--as-of", "2020-02-29T12:00:00+05:30"];

        (int exitCode, string output, _) = await RunAsync(before ? [.. asOf, .. view] : [.. view, .. asOf]);

        exitCode.ShouldBe(0);
        JsonNode facts = JsonNode.Parse(output)!["facts"]!;
        ((string)facts["asOf"]!["instant"]!).ShouldBe("2020-02-29T12:00:00+05:30");
        ((string)facts["asOf"]!["source"]!).ShouldBe("session-option");
        ((string)facts["rows"]![0]![1]!).ShouldBe("2020-02-29T12:00:00+05:30");
    }

    [Theory]
    [InlineData("search", "Patient")]
    [InlineData("inspect")]
    [InlineData("term", "list")]
    [InlineData("mcp")]
    public async Task GivenAsOfOnAVerbThatDoesNotReadIt_WhenParsed_ThenItIsRefused(params string[] verb)
    {
        using TestFolder folder = Clinic();
        string[] args = verb[0] == "term" ? ["term", verb[1], folder.Path, "--as-of", "2020-01-01T00:00:00Z"] : [verb[0], folder.Path, .. verb[1..], "--as-of", "2020-01-01T00:00:00Z"];

        (int exitCode, string output, string errors) = await RunAsync(args);

        exitCode.ShouldNotBe(0);
        output.ShouldNotContain("\"facts\"");
        errors.ShouldContain("--as-of is used only by draft and view for now");
    }

    [Fact]
    public async Task GivenAMalformedAsOf_WhenViewRuns_ThenTheEnvelopeHasTheRequestError()
    {
        using TestFolder folder = Clinic();

        (int exitCode, string output, _) = await RunAsync(["view", folder.Path, PatientView, "--no-cache", "--as-of", "yesterday"]);

        exitCode.ShouldNotBe(0);
        ((string)JsonNode.Parse(output)!["issues"]![0]!["code"]!).ShouldBe("invalid-as-of");
    }

    // The root as Program.cs builds it.
    private static async Task<(int ExitCode, string Output, string Errors)> RunAsync(string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(InspectCommand.Create());
        root.Subcommands.Add(SearchCommand.Create());
        root.Subcommands.Add(TermCommand.Create());
        root.Subcommands.Add(ViewCommand.Create());
        root.Subcommands.Add(DraftCommand.Create());
        root.Subcommands.Add(McpCommand.Create());
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
