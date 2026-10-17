// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b fix round 1, I3: <c>patient_context</c> root, expand and find read the session's <c>--as-of</c> as <c>patient_summary</c>
/// does, so one session gives one map. Regression: at 86a9d902 they ignored it and read the record as of the patient's latest dated
/// resource, so <c>mcp --as-of</c> gave two different maps of one patient.
/// </summary>
public class PatientContextAsOfTests
{
    private const string Sct = "http://snomed.info/sct";

    private static FolderSession Open(TestFolder folder, string? asOf) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: new ManualTimeProvider { Now = PatientFixture.AsOf }, asOfOption: asOf);

    private static JsonNode Json(CallToolResult result) => JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;

    // c1 before 2020, c2 after it.
    private static TestFolder Chart() => new TestFolder().Write("chart.ndjson", $$$"""
        {"resourceType":"Patient","id":"m1","birthDate":"1970-01-01","gender":"female"}
        {"resourceType":"Condition","id":"c1","subject":{"reference":"Patient/m1"},"clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"active"}]},"code":{"coding":[{"system":"{{{Sct}}}","code":"44054006","display":"Diabetes"}]},"onsetDateTime":"2015-01-01"}
        {"resourceType":"Condition","id":"c2","subject":{"reference":"Patient/m1"},"clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"active"}]},"code":{"coding":[{"system":"{{{Sct}}}","code":"38341003","display":"Hypertension"}]},"onsetDateTime":"2021-01-01"}
        """);

    [Fact]
    public void GivenTheSessionOption_WhenTheRootMapIsRead_ThenItIsTheMapPatientSummaryGives()
    {
        using TestFolder folder = Chart();
        var tools = new PatientTools(Open(folder, "2020-01-01"));

        JsonNode summary = Json(tools.Summary("m1"));
        JsonNode root = Json(tools.PatientContext("root", "m1"));

        root["facts"]!.ToJsonString().ShouldBe(summary["facts"]!.ToJsonString());
        root["asOf"]!.ToJsonString().ShouldBe(summary["asOf"]!.ToJsonString());
        ((int)root["facts"]!["domains"]!["problems"]!["count"]!).ShouldBe(1);
        ((string)root["asOf"]!["source"]!).ShouldBe(AsOfSources.SessionOption);
    }

    [Fact]
    public void GivenTheSessionOption_WhenTheFullRootIsRead_ThenItIsReadAsOfTheOption()
    {
        using TestFolder folder = Chart();

        JsonNode root = Json(new PatientTools(Open(folder, "2020-01-01")).PatientContext("root", "m1", detail: "full"));

        ((int)root["facts"]!["problems"]!["count"]!).ShouldBe(1);
        ((string)root["facts"]!["asOf"]!).ShouldStartWith("2020-01-01T00:00:00");
        ((string)root["asOf"]!["source"]!).ShouldBe(AsOfSources.SessionOption);
        ((string)root["asOf"]!["precision"]!).ShouldBe("date");
    }

    [Fact]
    public void GivenTheSessionOption_WhenANodeIsExpanded_ThenWhatIsDatedAfterItIsLeftOut()
    {
        using TestFolder folder = Chart();

        JsonNode node = Json(new PatientTools(Open(folder, "2020-01-01")).PatientContext("expand", "m1", nodeKey: "problems"));

        ((int)node["facts"]!["total"]!).ShouldBe(1);
        node["facts"]!["items"]!.AsArray().Select(item => (string)item!["reference"]!).ShouldBe(["Condition/c1"]);
        ((string)node["asOf"]!["source"]!).ShouldBe(AsOfSources.SessionOption);
    }

    [Fact]
    public void GivenTheSessionOption_WhenFound_ThenAMatchDatedAfterItIsNotListed()
    {
        using TestFolder folder = Chart();

        JsonNode found = Json(new PatientTools(Open(folder, "2020-01-01")).PatientContext("find", "m1", query: "38341003"));

        ((int)found["facts"]!["total"]!).ShouldBe(0);
        ((string)found["asOf"]!["source"]!).ShouldBe(AsOfSources.SessionOption);
    }

    [Fact]
    public void GivenNoSessionOption_WhenTheRootMapIsRead_ThenItIsStillReadAsOfThePatientsLatestDate()
    {
        using TestFolder folder = Chart();

        JsonNode root = Json(new PatientTools(Open(folder, asOf: null)).PatientContext("root", "m1"));

        ((int)root["facts"]!["domains"]!["problems"]!["count"]!).ShouldBe(2);
        ((string)root["asOf"]!["source"]!).ShouldBe(AsOfSources.PatientLatest);
    }

    [Fact]
    public void GivenAMalformedSessionOption_WhenTheContextIsRead_ThenEachOperationFailsWithInvalidAsOf()
    {
        using TestFolder folder = Chart();
        var tools = new PatientTools(Open(folder, "soon"));

        string[] codes =
        [
            (string)Json(tools.PatientContext("root", "m1"))["issues"]![0]!["code"]!,
            (string)Json(tools.PatientContext("root", "m1", detail: "full"))["issues"]![0]!["code"]!,
            (string)Json(tools.PatientContext("expand", "m1", nodeKey: "problems"))["issues"]![0]!["code"]!,
            (string)Json(tools.PatientContext("find", "m1", query: "38341003"))["issues"]![0]!["code"]!,
        ];

        codes.ShouldAllBe(code => code == AsOfClock.InvalidCode);
    }
}
