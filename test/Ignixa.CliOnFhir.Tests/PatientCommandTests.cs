// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Ignixa.CliOnFhir.Mcp;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The patient verbs and their MCP tools answer alike. New capability: at 523e80d neither existed. The verbs write to the
/// process-wide console, so these tests must not run beside others that do.
/// </summary>
[Collection("Console")]
public class PatientCommandTests
{
    [Theory]
    [InlineData("patients")]
    [InlineData("find")]
    [InlineData("node")]
    public async Task GivenTheSameRequest_WhenAskedOnTheCliAndOverMcp_ThenTheFactsAreTheSame(string verb)
    {
        using TestFolder folder = PatientFixture.Ndjson();
        var tools = new PatientTools(PatientFixture.Open(folder));
        (string[] args, CallToolResult mcp) = verb switch
        {
            "patients" => (new[] { "patients", folder.Path, "--sort", "latest", "--count", "2" }, tools.PatientList(count: 2, sort: "latest")),
            "find" => (new[] { "find", folder.Path, "p1", "asthma" }, tools.ContextFind("p1", "asthma")),
            _ => (new[] { "context", folder.Path, "p1", "--node", "problem/Condition-c1" }, tools.ContextExpand("p1", "problem/Condition-c1")),
        };

        (int exitCode, JsonNode cli) = await RunAsync(verb == "node" ? ContextCommand.Create() : verb == "find" ? FindCommand.Create() : PatientsCommand.Create(), [.. args, "--no-cache"]);
        JsonNode overMcp = JsonNode.Parse(mcp.Content.OfType<TextContentBlock>().Single().Text)!;

        exitCode.ShouldBe(0);
        mcp.IsError.ShouldBe(false);
        WithoutClock(cli["facts"]!).ToJsonString().ShouldBe(WithoutClock(overMcp["facts"]!).ToJsonString());
        cli["next"]!.ToJsonString().ShouldBe(overMcp["next"]!.ToJsonString());
    }

    [Fact]
    public async Task GivenAnUnknownNodeKey_WhenExpandedOnTheCliAndOverMcp_ThenBothFailWithTheSameIssue()
    {
        using TestFolder folder = PatientFixture.Ndjson();

        (int exitCode, JsonNode cli) = await RunAsync(ContextCommand.Create(), "context", folder.Path, "p1", "--node", "problem/Condition-zz", "--no-cache");
        CallToolResult mcp = new PatientTools(PatientFixture.Open(folder)).ContextExpand("p1", "problem/Condition-zz");

        exitCode.ShouldBe(1);
        mcp.IsError.ShouldBe(true);
        cli["facts"].ShouldBeNull();
        string issue = cli["issues"]![0]!["diagnostics"]!.GetValue<string>();
        issue.ShouldStartWith("'problem/Condition-zz' names no node in Patient/p1's context as of ");
        ((string)cli["issues"]![0]!["code"]!).ShouldBe("unknown-node-key");
        ((string)JsonNode.Parse(mcp.Content.OfType<TextContentBlock>().Single().Text)!["issues"]![0]!["code"]!).ShouldBe("unknown-node-key");
    }

    [Fact]
    public void GivenPagingWithoutANode_WhenTheContextVerbRuns_ThenItIsRejected()
    {
        using TestFolder folder = PatientFixture.Ndjson();
        var root = new RootCommand();
        root.Subcommands.Add(ContextCommand.Create());

        ParseResult parsed = root.Parse(["context", folder.Path, "p1", "--count", "3", "--no-cache"]);

        parsed.Errors.ShouldHaveSingleItem().Message.ShouldBe("--count, --offset and --elements apply only with --node; the root digest is not paged.");
    }

    /// <summary>The facts without the call's <c>asOf</c>, which follows each session's clock.</summary>
    private static JsonNode WithoutClock(JsonNode facts)
    {
        JsonNode copy = facts.DeepClone();
        copy.AsObject().Remove("asOf");
        return copy;
    }

    private static async Task<(int ExitCode, JsonNode Output)> RunAsync(Command command, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(command);
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
