// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The server's response ceiling (16 KB over MCP, 50 KB on the CLI) and the order of <c>next</c> on a cut page: the calls that
/// answer over every match in one response come before the call for more pages. New behaviour: at d3498e32 every server allowed
/// 50 KB, which agent clients move to a file, and <c>next</c> started with the call for the next page.
/// </summary>
public class ResponseCeilingTests
{
    private const int ObservationCount = 20;

    /// <summary>Patients <c>p1</c> and <c>p2</c> with 20 observations of about 1.2 KB each between them, so a page of 20 is cut at 12 KB.</summary>
    private static TestFolder Chart() => new TestFolder()
        .Write("patients.ndjson", """
            {"resourceType":"Patient","id":"p1"}
            {"resourceType":"Patient","id":"p2"}
            """)
        .Write("obs.ndjson", string.Join('\n', Enumerable.Range(0, ObservationCount).Select(i =>
            $$"""{"resourceType":"Observation","id":"o{{i:00}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"4548-4"}]},"subject":{"reference":"Patient/p{{i % 2 + 1}}"},"effectiveDateTime":"2020-01-{{i + 1:00}}","valueQuantity":{"value":{{i}},"unit":"%"},"note":[{"text":"{{new string('n', 700)}}"}]}""")));

    private static FolderSession Open(TestFolder folder, int maxResponseBytes) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, maxResponseBytes: maxResponseBytes);

    [Fact]
    public void GivenTheMcpCeiling_WhenACallAsksForFiftyKilobytes_ThenItIsClampedToSixteenWithAWarningAndNoLargerBudgetIsOffered()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, EnvelopeJson.McpMaxBytes), new SearchRequest("Observation", Count: ObservationCount, MaxBytes: EnvelopeJson.MaxBytes));

        IssueItem clamped = envelope.Issues.ShouldHaveSingleItem();
        clamped.Code.ShouldBe("max-bytes-clamped");
        clamped.Diagnostics.ShouldContain($"maximum of {EnvelopeJson.McpMaxBytes}");
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.McpMaxBytes);
        EnvelopeJson.SizeOf(envelope).ShouldBeGreaterThan(EnvelopeJson.DefaultBudget);
        envelope.Next.ShouldNotContain(call => call.Hint != null && call.Hint.Contains("largest budget", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenTheMcpCeiling_WhenTheDefaultBudgetCutsAPage_ThenTheLargerBudgetOfferedIsTheCeiling()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, EnvelopeJson.McpMaxBytes), new SearchRequest("Observation", Count: ObservationCount));

        envelope.Next.Single(call => call.Args.ContainsKey("maxBytes")).Args["maxBytes"].ShouldBe(EnvelopeJson.McpMaxBytes);
    }

    [Fact]
    public void GivenACeilingBelowTheDefaultBudget_WhenSearchedWithoutMaxBytes_ThenTheCeilingIsTheBudget()
    {
        using TestFolder folder = Chart();
        const int ceiling = 6 * 1024;

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, ceiling), new SearchRequest("Observation", Count: ObservationCount));

        envelope.Facts.ShouldNotBeNull().Returned.ShouldBeGreaterThan(0);
        envelope.Issues.ShouldBeEmpty();
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(ceiling);
    }

    [Theory]
    [InlineData(EnvelopeJson.MinBudget - 1)]
    [InlineData(EnvelopeJson.MaxBytes + 1)]
    public void GivenACeilingOutsideTheAllowedRange_WhenTheSessionIsCreated_ThenItIsRejected(int ceiling)
    {
        using TestFolder folder = Chart();

        Should.Throw<ArgumentOutOfRangeException>(() => Open(folder, ceiling));
    }

    [Fact]
    public void GivenACutPageAcrossPatients_WhenSearched_ThenNextOffersFhirPathPerPatientAndCountBeforeTheNextPage()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, EnvelopeJson.McpMaxBytes), new SearchRequest("Observation", Count: ObservationCount));

        envelope.Next.Count.ShouldBeGreaterThan(4);
        envelope.Next[0].Args["fhirpath"].ShouldBe("entry.count()");
        envelope.Next[0].Hint.ShouldNotBeNull().ShouldContain("one value instead of pages");
        envelope.Next[1].Args["perPatient"].ShouldBe(PerPatient.Latest);
        envelope.Next[1].Args.ShouldNotContainKey("offset");
        envelope.Next[2].Args["summary"].ShouldBe("count");
        envelope.Next[3].Args.ShouldContainKey("offset");
        envelope.Next[3].Hint.ShouldBeNull();
    }

    [Fact]
    public void GivenTheCohortCallsFromNext_WhenFollowed_ThenEachAnswersInOneResponse()
    {
        using TestFolder folder = Chart();
        FolderSession session = Open(folder, EnvelopeJson.McpMaxBytes);
        IReadOnlyList<NextCall> next = ResourceSearcher.Search(session, new SearchRequest("Observation", Count: ObservationCount)).Next;

        ToolEnvelope<SearchFacts?> value = ResourceSearcher.Search(session, new SearchRequest("Observation", FhirPath: (string)next[0].Args["fhirpath"]));
        ToolEnvelope<SearchFacts?> perPatient = ResourceSearcher.Search(session, new SearchRequest("Observation", Count: (int)next[1].Args["count"], PerPatient: (string)next[1].Args["perPatient"]));

        value.Facts.ShouldNotBeNull().Value.ShouldNotBeNull().ToJsonString().ShouldBe($"[{ObservationCount}]");
        Ids(perPatient).ShouldBe(["o19", "o18"]);
        perPatient.Omitted.ShouldBeEmpty();
    }

    [Fact]
    public void GivenACutPageInOnePatientsCompartment_WhenSearched_ThenNextOffersNoPerPatientCall()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder, EnvelopeJson.McpMaxBytes), new SearchRequest("Observation", Count: ObservationCount, Compartment: "Patient/p1", MaxBytes: 6 * 1024));

        envelope.Next.ShouldNotContain(call => call.Args.ContainsKey("perPatient"));
        envelope.Next[0].Args.ShouldContainKey("fhirpath");
    }

    private static string[] Ids(ToolEnvelope<SearchFacts?> envelope) => SearchFixture.Ids(envelope);
}
