// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
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
    public async Task GivenACompartmentOnTheCli_WhenRun_ThenTheSearchIsLimitedToItAndTheFactsSaySo()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "Observation", "--compartment", "Patient/p2", "--no-cache");

        exitCode.ShouldBe(0);
        ((int)output["facts"]!["total"]!).ShouldBe(2);
        ((string)output["facts"]!["compartment"]!).ShouldBe("Patient/p2");
    }

    [Fact]
    public async Task GivenUnsupportedSearch_WhenRun_ThenIssueJsonIsPrintedAndExitCodeIsNonZero()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "Patient", "_text=Smith", "--no-cache");

        exitCode.ShouldNotBe(0);
        output["facts"].ShouldBeNull();
        ((string)output["issues"]![0]!["code"]!).ShouldBe("search-not-supported");
    }

    [Fact]
    public async Task GivenLenientOnTheCli_WhenAnUnknownParameterIsPassed_ThenTheSearchSucceedsWithAWarning()
    {
        using TestFolder folder = Clinic();

        (int strictExit, JsonNode strict) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "Patient", "Family=Smith", "--no-cache");
        (int lenientExit, JsonNode lenient) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "Patient", "Family=Smith", "--lenient", "--no-cache");

        strictExit.ShouldNotBe(0);
        ((string)strict["issues"]![0]!["code"]!).ShouldBe("unknown-search-parameter");
        lenientExit.ShouldBe(0);
        ((int)lenient["facts"]!["total"]!).ShouldBe(3);
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
        ((string)output["evidence"]!["files"]![(int)output["evidence"]!["items"]![0]!["file"]!]!).ShouldBe(PatientsFile);
    }

    [Fact]
    public async Task GivenMalformedReference_WhenGet_ThenExitCodeIsNonZero()
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync(GetCommand.Create(), "get", folder.Path, "nonsense", "--no-cache");

        exitCode.ShouldNotBe(0);
        ((string)output["issues"]![0]!["code"]!).ShouldBe("invalid-reference");
    }

    [Fact]
    public async Task GivenDefaultOutput_WhenRun_ThenJsonIsCompactAndPrettyIsOptIn()
    {
        using TestFolder folder = Clinic();

        (_, _, string compact) = await RunWithTextAsync(SearchCommand.Create(), "search", folder.Path, "Patient", "--no-cache");
        (_, _, string pretty) = await RunWithTextAsync(SearchCommand.Create(), "search", folder.Path, "Patient", "--no-cache", "--pretty");

        compact.TrimEnd().ShouldNotContain('\n');
        pretty.TrimEnd().ShouldContain('\n');
    }

    // CLI parity: --columns and --fhirpath print what the MCP tool's columns and fhirpath return for the same search.
    [Fact]
    public async Task GivenColumnsOnTheCli_WhenRun_ThenTheTableMatchesTheSearchersAndResolvesTheRequester()
    {
        using TestFolder folder = ProjectionFixture.Synthea();
        const string Columns = """{"date":"authoredOn","drug":"(medication.ofType(CodeableConcept) | medication.ofType(Reference).resolve().code).coding.first().display","prescriber":"requester.resolve().name.first().family"}""";
        ToolEnvelope<SearchFacts?> direct = ResourceSearcher.Search(Open(folder), new SearchRequest("MedicationRequest", "_sort=authoredon", Compartment: "Patient/pa", Columns: SearchColumn.FromJson(Columns)));

        (int exitCode, JsonNode output) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "MedicationRequest", "_sort=authoredon", "--compartment", "Patient/pa", "--columns", Columns, "--no-cache");

        exitCode.ShouldBe(0);
        output["facts"]!.ToJsonString().ShouldBe(JsonSerializer.SerializeToNode(direct.Facts, EnvelopeJson.Compact)!.ToJsonString());
        ((string)output["facts"]!["rows"]![0]![3]!).ShouldBe("Marquardt");
    }

    [Fact]
    public async Task GivenFhirPathOnTheCli_WhenRun_ThenTheValueIsPrinted()
    {
        using TestFolder folder = ProjectionFixture.Synthea();

        (int exitCode, JsonNode output) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "Observation", "code=http://loinc.org|4548-4", "--compartment", "Patient/pa", "--fhirpath", "entry.resource.value.ofType(Quantity).value.min()", "--no-cache");

        exitCode.ShouldBe(0);
        output["facts"]!["value"]!.ToJsonString().ShouldBe("[6.2]");
        ((string)output["facts"]!["valueFrom"]!).ShouldBe("result-elements");
    }

    [Theory]
    [InlineData("--columns", "[\"status\"]")]
    [InlineData("--columns", "{\"bad\":\"status.(\"}")]
    [InlineData("--fhirpath", "entry.(")]
    public async Task GivenAnInvalidProjectionOnTheCli_WhenRun_ThenInvalidParameterIsPrintedAndExitCodeIsNonZero(string option, string value)
    {
        using TestFolder folder = Clinic();

        (int exitCode, JsonNode output) = await RunAsync(SearchCommand.Create(), "search", folder.Path, "Patient", option, value, "--no-cache");

        exitCode.ShouldNotBe(0);
        output["facts"].ShouldBeNull();
        ((string)output["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
    }

    [Fact]
    public async Task GivenFolderThatDoesNotExist_WhenSearched_ThenIssueJsonIsPrintedAndExitCodeIsNonZero()
    {
        string missing = Path.Combine(Path.GetTempPath(), "cli-on-fhir-tests", Guid.NewGuid().ToString("N"));

        (int exitCode, JsonNode output) = await RunAsync(SearchCommand.Create(), "search", missing, "Patient", "--no-cache");

        exitCode.ShouldNotBe(0);
        ((string)output["issues"]![0]!["code"]!).ShouldBe("folder-unavailable");
    }

    private static async Task<(int ExitCode, JsonNode Output)> RunAsync(Command command, params string[] args)
    {
        (int exitCode, JsonNode output, _) = await RunWithTextAsync(command, args);
        return (exitCode, output);
    }

    private static async Task<(int ExitCode, JsonNode Output, string Text)> RunWithTextAsync(Command command, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(command);
        TextWriter original = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            int exitCode = await root.Parse(args).InvokeAsync();
            return (exitCode, JsonNode.Parse(captured.ToString())!, captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
