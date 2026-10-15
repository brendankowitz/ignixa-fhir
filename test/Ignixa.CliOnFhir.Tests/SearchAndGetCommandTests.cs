// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>The verbs write to the process-wide console, so these tests must not run beside others that do.</summary>
[Collection("Console")]
public class SearchAndGetCommandTests
{
    [Fact]
    public async Task GivenAnswerableSearch_WhenRun_ThenEnvelopeIsPrintedAndExitCodeIsZero()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "Patient", "family=Smith", "--no-cache");

        exitCode.ShouldBe(0);
        ((int)output["facts"]!["total"]!).ShouldBe(2);
        output["issues"]!.AsArray().ShouldBeEmpty();
        output["derived"].ShouldBeNull();
    }

    [Fact]
    public async Task GivenUnsupportedSearch_WhenRun_ThenIssueJsonIsPrintedAndExitCodeIsNonZero()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "Patient", "_sort=family", "--no-cache");

        exitCode.ShouldNotBe(0);
        output["facts"].ShouldBeNull();
        ((string)output["issues"]![0]!["code"]!).ShouldBe("search-not-supported");
    }

    [Fact]
    public async Task GivenSearchWithoutQuery_WhenRun_ThenEveryResourceOfTheTypeIsCounted()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "Observation", "--summary", "count", "--no-cache");

        exitCode.ShouldBe(0);
        ((int)output["facts"]!["total"]!).ShouldBe(5);
        output["facts"]!.AsObject().ContainsKey("resources").ShouldBeFalse();
    }

    [Fact]
    public async Task GivenPagingAndElementsOptions_WhenRun_ThenTheyReachTheSearch()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "Patient", "--count", "1", "--offset", "1", "--elements", "gender", "--no-cache");

        exitCode.ShouldBe(0);
        ((string)output["facts"]!["resources"]![0]!["id"]!).ShouldBe("p2");
        output["facts"]!["resources"]![0]!.AsObject().ContainsKey("name").ShouldBeFalse();
        ((string)output["omitted"]![0]!["what"]!).ShouldBe("1 more matches (offset=2)");
    }

    [Fact]
    public async Task GivenReferencesWithAMissingOne_WhenGet_ThenAbsentIsPrintedAndExitCodeIsZero()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync(GetCommand.Create(), "get", folder.Path, "Patient/p1", "Patient/none", "--no-cache");

        exitCode.ShouldBe(0);
        ((string)output["absent"]![0]!["what"]!).ShouldBe("Patient/none");
        ((string)output["evidence"]![0]!["file"]!).ShouldBe(PatientsFile);
    }

    [Fact]
    public async Task GivenMalformedReference_WhenGet_ThenExitCodeIsNonZero()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync(GetCommand.Create(), "get", folder.Path, "nonsense", "--no-cache");

        exitCode.ShouldNotBe(0);
        ((string)output["issues"]![0]!["code"]!).ShouldBe("invalid-reference");
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
