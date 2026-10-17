// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Cohorts;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Drafting;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b: every envelope states the asOf its answer is anchored to, and the MCP session's <c>--as-of</c> reaches every call that
/// does not give its own. New behaviour: before it no envelope had an <c>asOf</c> and <c>mcp</c> refused <c>--as-of</c>.
/// </summary>
public class EnvelopeAsOfTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static FolderSession Open(TestFolder folder, string? asOf = null) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: new ManualTimeProvider { Now = Now }, asOfOption: asOf);

    private static JsonNode Json(CallToolResult result) => JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;

    private static TestFolder Chart() => new TestFolder().Write("chart.ndjson", """
        {"resourceType":"Patient","id":"m1","birthDate":"1970-01-01","gender":"female"}
        {"resourceType":"Encounter","id":"e1","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"subject":{"reference":"Patient/m1"},"period":{"start":"2015-01-01T10:00:00Z"}}
        """);

    [Fact]
    public void GivenNoAsOf_WhenATimeIndependentToolAnswers_ThenItsEnvelopeStatesTheSessionsClock()
    {
        using TestFolder folder = SearchFixture.Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient"));

        envelope.AsOf.ShouldBe(new AsOf(Now, AsOfSources.Clock));
    }

    [Fact]
    public void GivenTheSessionOption_WhenAnMcpSearchAnswers_ThenItsEnvelopeStatesTheOptionWithItsPrecision()
    {
        using TestFolder folder = SearchFixture.Clinic();

        JsonNode asOf = Json(new ResourceTools(Open(folder, "2020-01-01")).SearchFhirResources("Patient"))["asOf"]!;

        ((string)asOf["instant"]!).ShouldBe("2020-01-01T00:00:00Z");
        ((string)asOf["source"]!).ShouldBe(AsOfSources.SessionOption);
        ((string)asOf["precision"]!).ShouldBe("date");
        asOf["zoneAssumed"].ShouldBeNull();
    }

    [Fact]
    public void GivenAFolderThatCannotBeRead_WhenAToolFails_ThenAsOfIsWrittenAsNullOrAsTheOption()
    {
        string missing = Path.Combine(Path.GetTempPath(), "cli-on-fhir-missing-" + Guid.NewGuid().ToString("N"));

        JsonNode withoutOption = Json(new ResourceTools(new FolderSession(missing, new R4CoreSchemaProvider(), NullLoggerFactory.Instance)).SearchFhirResources("Patient"));
        JsonNode withOption = Json(new ResourceTools(new FolderSession(missing, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, asOfOption: "2020-01-01T00:00:00Z")).SearchFhirResources("Patient"));

        withoutOption.AsObject().ContainsKey("asOf").ShouldBeTrue();
        withoutOption["asOf"].ShouldBeNull();
        ((string)withOption["asOf"]!["source"]!).ShouldBe(AsOfSources.SessionOption);
    }

    [Fact]
    public void GivenTheSessionOption_WhenPatientSummaryIsCalledWithoutAsOf_ThenTheMapIsReadAsOfTheOption()
    {
        using TestFolder folder = Chart();

        JsonNode envelope = Json(new PatientTools(Open(folder, "2014-06-01")).Summary("m1"));

        ((string)envelope["facts"]!["record"]!["asOfSource"]!).ShouldBe(AsOfSources.SessionOption);
        ((int)envelope["facts"]!["domains"]!["encounters"]!["count"]!).ShouldBe(0);
        ((string)envelope["asOf"]!["source"]!).ShouldBe(AsOfSources.SessionOption);
    }

    [Fact]
    public void GivenTheSessionOptionAndAnArgument_WhenPatientSummaryIsCalled_ThenTheArgumentWins()
    {
        using TestFolder folder = Chart();

        JsonNode envelope = Json(new PatientTools(Open(folder, "2014-06-01")).Summary("m1", asOf: "2016-01-01"));

        ((string)envelope["facts"]!["record"]!["asOfSource"]!).ShouldBe(AsOfSources.Argument);
        ((int)envelope["facts"]!["domains"]!["encounters"]!["count"]!).ShouldBe(1);
        ((string)envelope["asOf"]!["instant"]!).ShouldBe("2016-01-01T00:00:00Z");
    }

    [Fact]
    public void GivenNoAsOf_WhenSummarized_ThenTheEnvelopeStatesThePatientsLatestDate()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Summarize(Open(folder), "m1");

        envelope.AsOf.ShouldBe(new AsOf(new DateTimeOffset(2015, 1, 1, 10, 0, 0, TimeSpan.Zero), AsOfSources.PatientLatest));
    }

    [Fact]
    public async Task GivenNoAsOf_WhenACohortIsCounted_ThenTheEnvelopeStatesTheClockAsTheFactsDo()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<CohortFacts?> envelope = await PatientCohort.RunAsync(Open(folder), new CohortArguments(), CancellationToken.None);

        envelope.Facts.ShouldNotBeNull().AsOfSource.ShouldBe(AsOfSources.Clock);
        envelope.AsOf.ShouldBe(new AsOf(Now, AsOfSources.Clock));
    }

    [Fact]
    public async Task GivenTheSessionOption_WhenACohortIsCountedWithoutAsOf_ThenItIsCountedAsOfTheOption()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<CohortFacts?> envelope = await PatientCohort.RunAsync(Open(folder, "2000-01-01"), new CohortArguments(AgeMin: 31), CancellationToken.None);

        CohortFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.AsOf.ShouldBe("2000-01-01");
        facts.AsOfSource.ShouldBe(AsOfSources.SessionOption);
        facts.Count.ShouldBe(0);
        envelope.AsOf.ShouldNotBeNull().Source.ShouldBe(AsOfSources.SessionOption);
    }

    [Fact]
    public async Task GivenADraftsOwnAsOf_WhenDrafted_ThenItDatesTheDraftAndTheEnvelopeSaysArgument()
    {
        using TestFolder folder = DraftFixture.Clinic();
        FolderSession session = DraftFixture.Open(folder);

        ToolEnvelope<DraftFacts?> envelope = await DraftService.RunAsync(session, new DraftRequest("MedicationRequest", DraftFixture.PotassiumOrder, AsOf: "2023-03-04"), new AsOfClock(session, DraftFixture.Instant), NullLogger.Instance, CancellationToken.None);

        DraftFixture.Text(envelope.Facts.ShouldNotBeNull().Resource["authoredOn"]).ShouldBe("2023-03-04");
        envelope.AsOf.ShouldBe(AsOf.OfDate(new DateOnly(2023, 3, 4), AsOfSources.Argument));
    }

    [Fact]
    public async Task GivenAMalformedDraftAsOf_WhenDrafted_ThenTheCallFailsAndTheEnvelopeStatesTheSessionsAsOf()
    {
        using TestFolder folder = DraftFixture.Clinic();
        FolderSession session = DraftFixture.Open(folder);

        ToolEnvelope<DraftFacts?> envelope = await DraftService.RunAsync(session, new DraftRequest("MedicationRequest", DraftFixture.PotassiumOrder, AsOf: "soon"), new AsOfClock(session, DraftFixture.Instant), NullLogger.Instance, CancellationToken.None);

        DraftFixture.Error(envelope).Code.ShouldBe(AsOfClock.InvalidCode);
        envelope.AsOf.ShouldNotBeNull().Source.ShouldBe(AsOfSources.SessionOption);
    }

    [Fact]
    public void GivenAnAsOfAlreadyResolvedOrOnlyStated_WhenAskedWithoutReading_ThenItIsGivenAndNullOtherwise()
    {
        using TestFolder folder = SearchFixture.Clinic();
        FolderSession unresolved = Open(folder);
        FolderSession resolved = Open(folder);
        _ = resolved.AsOfClock.Resolve();

        unresolved.AsOfClock.WithoutReading().ShouldBeNull();
        resolved.AsOfClock.WithoutReading().ShouldBe(new AsOf(Now, AsOfSources.Clock));
        Open(folder, "2020-01-01T00:00:00Z").AsOfClock.WithoutReading().ShouldNotBeNull().Source.ShouldBe(AsOfSources.SessionOption);
        Open(folder, "soon").AsOfClock.WithoutReading().ShouldBeNull();
    }
}
