// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>The synthetic <c>_lastUpdated</c> index entry: FHIR instants only, independent of the machine's time zone.</summary>
public class LastUpdatedTests
{
    private const string Patients = """
        {"resourceType":"Patient","id":"z","meta":{"lastUpdated":"2020-01-01T23:30:00Z"}}
        {"resourceType":"Patient","id":"off","meta":{"lastUpdated":"2020-01-02T01:30:00+02:00"}}
        {"resourceType":"Patient","id":"notz","meta":{"lastUpdated":"2020-01-01T23:30:00"}}
        {"resourceType":"Patient","id":"date","meta":{"lastUpdated":"2020-01-01"}}
        {"resourceType":"Patient","id":"none"}
        {"resourceType":"Patient","id":"bad","meta":{"lastUpdated":"garbage"}}
        """;

    [Fact]
    public void GivenInstantsWithAndWithoutZones_WhenSearched_ThenOnlyValidInstantsAreIndexedAndTheRestAreReported()
    {
        using TestFolder folder = new TestFolder().Write("p.ndjson", Patients);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_lastUpdated=ge2020-01-01T23:00:00Z"));

        Ids(envelope).ShouldBe(["z", "off"]);
        IssueItem[] problems = [.. envelope.Issues.Where(issue => issue.Code == "index-extraction-failed")];
        problems.Length.ShouldBe(3);
        problems.Select(issue => issue.Diagnostics).ShouldContain(message => message.Contains("Patient/notz", StringComparison.Ordinal));
        problems.Select(issue => issue.Diagnostics).ShouldContain(message => message.Contains("Patient/date", StringComparison.Ordinal));
        problems.Select(issue => issue.Diagnostics).ShouldContain(message => message.Contains("Patient/bad", StringComparison.Ordinal));
        problems.ShouldAllBe(issue => issue.Location!.StartsWith("p.ndjson:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Pacific/Auckland")]
    [InlineData("America/Los_Angeles")]
    public void GivenAnyLocalTimeZone_WhenSearchedByLastUpdated_ThenResultsDoNotDependOnIt(string timeZone)
    {
        using TestFolder folder = new TestFolder().Write("p.ndjson", Patients);

        string output = RunCli(timeZone, "search", folder.Path, "Patient", "_lastUpdated=ge2020-01-01T23:00:00Z", "--no-cache");

        System.Text.Json.Nodes.JsonNode envelope = System.Text.Json.Nodes.JsonNode.Parse(output)!;
        envelope["facts"]!["resources"]!.AsArray().Select(resource => (string)resource!["id"]!).ShouldBe(["z", "off"]);
    }

    /// <summary>Runs the tool in a child process so the time zone can differ without touching this process.</summary>
    private static string RunCli(string timeZone, params string[] args)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "cli-on-fhir.dll");
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(dll);
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["TZ"] = timeZone;
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, error);
        return output;
    }
}
