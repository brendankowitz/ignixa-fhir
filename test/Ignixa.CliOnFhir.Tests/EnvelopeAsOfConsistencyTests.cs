// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Views;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.ViewFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b fix round 1: the envelope's <c>asOf</c> agrees with what the answer used. M3: a refused call keeps the asOf it was
/// given. M4: patient_cohort states one instant, to the second, in facts and envelope. M5: the patient map's
/// <c>record.window</c> is written as a UTC instant. M6: a view's envelope states the %asOf it read. Regressions: at 86a9d902 a
/// refused call stated the session's asOf, the cohort's envelope kept the clock's fraction of a second that facts.asOf dropped,
/// the window was written as <c>+00:00</c>, and a view run with <c>--var asOf</c> stated the session's asOf.
/// </summary>
public class EnvelopeAsOfConsistencyTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    private static FolderSession Open(TestFolder folder, string? asOf = null) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: new ManualTimeProvider { Now = Now }, asOfOption: asOf);

    private static JsonNode Json(CallToolResult result) => JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;

    private static void ShouldStateTheArgument(JsonNode envelope, string instant)
    {
        envelope["facts"].ShouldBeNull();
        ((string)envelope["asOf"]!["source"]!).ShouldBe(AsOfSources.Argument);
        ((string)envelope["asOf"]!["instant"]!).ShouldBe(instant);
    }

    [Fact]
    public void GivenAnAsOfArgument_WhenASearchOrPlanIsRefused_ThenItsEnvelopeStatesThatAsOf()
    {
        using TestFolder folder = SearchFixture.Clinic();
        var tools = new ResourceTools(Open(folder));

        ShouldStateTheArgument(Json(tools.SearchFhirResources("Observation", query: "nosuch=1", asOf: "2020-01-01")), "2020-01-01T00:00:00Z");
        ShouldStateTheArgument(Json(tools.PlanFhirSearch("Observation", query: "nosuch=1", asOf: "2020-01-01")), "2020-01-01T00:00:00Z");
    }

    [Fact]
    public async Task GivenAnAsOfArgument_WhenAPatientToolIsRefused_ThenItsEnvelopeStatesThatAsOf()
    {
        using TestFolder folder = SearchFixture.Clinic();
        var tools = new PatientTools(Open(folder));

        ShouldStateTheArgument(Json(tools.Summary("nobody", asOf: "2020-01-01")), "2020-01-01T00:00:00Z");
        ShouldStateTheArgument(Json(await tools.PatientCohort(asOf: "2020-01-01", gender: "nonsense")), "2020-01-01T00:00:00Z");
    }

    [Fact]
    public async Task GivenTheClockDefault_WhenACohortIsCounted_ThenFactsAndTheEnvelopeStateOneInstantToTheSecond()
    {
        using TestFolder folder = SearchFixture.Clinic();

        JsonNode envelope = Json(await new PatientTools(Open(folder)).PatientCohort());

        ((string)envelope["facts"]!["asOf"]!).ShouldBe("2026-10-15T12:00:00Z");
        ((string)envelope["asOf"]!["instant"]!).ShouldBe("2026-10-15T12:00:00Z");
    }

    [Fact]
    public async Task GivenAStatedAsOfWithAFractionAndAnOffset_WhenACohortIsCounted_ThenTheEnvelopeStatesTheSecondCountedInItsOffset()
    {
        using TestFolder folder = SearchFixture.Clinic();

        JsonNode envelope = Json(await new PatientTools(Open(folder)).PatientCohort(asOf: "2024-05-01T08:30:00.5-05:00"));

        ((string)envelope["facts"]!["asOf"]!).ShouldBe("2024-05-01T13:30:00Z");
        ((string)envelope["asOf"]!["instant"]!).ShouldBe("2024-05-01T08:30:00-05:00");
    }

    [Fact]
    public void GivenAnAsOf_WhenTheMapIsWritten_ThenItsWindowIsAUtcInstant()
    {
        using TestFolder folder = SearchFixture.Clinic();

        JsonNode envelope = Json(new PatientTools(Open(folder)).Summary("p1", asOf: "2020-01-01"));

        ((string)envelope["facts"]!["record"]!["window"]!["to"]!).ShouldBe("2020-01-01T00:00:00Z");
    }

    [Theory]
    [InlineData("asOf=2026-01-01T00:00:00Z", "2026-01-01T00:00:00Z", AsOfSources.Variable)]
    public async Task GivenAVariableAsOf_WhenAViewRuns_ThenTheEnvelopeStatesTheAsOfTheViewRead(string variable, string instant, string source)
    {
        using TestFolder folder = SearchFixture.Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(Open(folder), new ViewRequest(PatientView, Variables: [variable]), "2016-01-01T00:00:00Z");

        envelope.AsOf.ShouldNotBeNull().Instant.ShouldBe(instant);
        envelope.AsOf.Source.ShouldBe(source);
        envelope.Facts.ShouldNotBeNull().AsOf.Instant.ShouldBe(instant);
    }

    [Fact]
    public async Task GivenAViewConstantAsOf_WhenAViewRuns_ThenTheEnvelopeStatesIt()
    {
        using TestFolder folder = SearchFixture.Clinic();
        const string view = """
            {"resourceType":"ViewDefinition","resource":"Patient","constant":[{"name":"asOf","valueDateTime":"2016-01-01T00:00:00Z"}],
             "where":[{"path":"birthDate <= (%asOf - 40 years)"}],"select":[{"column":[{"name":"id","path":"id"}]}]}
            """;

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(Open(folder), new ViewRequest(view), "2026-01-01T00:00:00Z");

        envelope.AsOf.ShouldBe(new AsOf(new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero), AsOfSources.ViewConstant));
    }

    [Fact]
    public async Task GivenNoOverride_WhenAViewRuns_ThenTheEnvelopeStatesTheSessionsAsOf()
    {
        using TestFolder folder = SearchFixture.Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(Open(folder), new ViewRequest(PatientView), "2026-01-01T00:00:00Z");

        envelope.AsOf.ShouldNotBeNull().Source.ShouldBe(AsOfSources.SessionOption);
    }
}
