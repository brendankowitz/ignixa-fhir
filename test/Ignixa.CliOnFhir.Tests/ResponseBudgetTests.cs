// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Search;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The 12 KB default response budget, the <c>maxBytes</c> that raises it, the calls <c>next</c> offers when a page is cut,
/// the trimmed <c>meta</c> and the per-file evidence table. New behaviour: at f8b47276 the budget was 50 KB, <c>next</c> was
/// empty on a cut search page, <c>meta.profile</c> repeated on every trimmed resource and every evidence item named its file.
/// </summary>
public class ResponseBudgetTests
{
    private const string Profile = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-observation-lab";

    private const int ObservationCount = 20;

    /// <summary>Patient <c>p1</c> with 20 observations of about 1.2 KB each (cut at 12 KB, whole at 50 KB, well inside 12 KB trimmed), all in one file.</summary>
    private static TestFolder Chart() => new TestFolder()
        .Write("patient.json", """{"resourceType":"Patient","id":"p1"}""")
        .Write("obs.ndjson", string.Join('\n', Enumerable.Range(0, ObservationCount).Select(i =>
            $$"""{"resourceType":"Observation","id":"o{{i:00}}","meta":{"profile":["{{Profile}}"]},"status":"final","code":{"coding":[{"system":"http://loinc.org","code":"4548-4"}]},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"2020-01-{{i % 28 + 1:00}}","valueQuantity":{"value":{{i}},"unit":"%"},"note":[{"text":"{{new string('n', 700)}}"}]}""")));

    private static SearchRequest Observations(int? maxBytes = null, string? elements = null, int? count = ObservationCount) =>
        new("Observation", Compartment: "Patient/p1", Count: count, Elements: elements, MaxBytes: maxBytes);

    [Fact]
    public void GivenAPageLargerThanTheDefaultBudget_WhenSearched_ThenItIsCutWithinTwelveKilobytesAndOmittedCountsTheRest()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Observations());

        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.DefaultBudget);
        facts.Total.ShouldBe(ObservationCount);
        facts.Returned.ShouldBeInRange(1, ObservationCount - 1);
        envelope.Omitted.ShouldHaveSingleItem().ShouldBe(new OmittedItem($"{ObservationCount - facts.Returned} more matches (offset={facts.Returned})", "size-budget"));
    }

    [Fact]
    public void GivenACutPage_WhenSearched_ThenNextHoldsTheExactCallForTheRest()
    {
        using TestFolder folder = Chart();
        var session = Open(folder);
        ToolEnvelope<SearchFacts?> first = ResourceSearcher.Search(session, Observations());
        int returned = first.Facts.ShouldNotBeNull().Returned;

        NextCall rest = first.Next[0];
        ToolEnvelope<SearchFacts?> second = ResourceSearcher.Search(session, Request(rest.Args));

        rest.Tool.ShouldBe("search_fhir_resources");
        rest.Args["offset"].ShouldBe(returned);
        rest.Args["compartment"].ShouldBe("Patient/p1");
        Ids(second).First().ShouldBe($"o{returned:00}");
        second.Facts.ShouldNotBeNull().Offset.ShouldBe(returned);
    }

    [Fact]
    public void GivenACutPage_WhenSearched_ThenNextOffersNarrowingByElementsDateRangeAndCountEachWithAHint()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Observations());

        NextCall elements = envelope.Next.Single(call => call.Args.ContainsKey("elements"));
        elements.Args["elements"].ShouldBe("code,value[x],effective[x]");
        elements.Args.ShouldNotContainKey("offset");
        elements.Hint.ShouldNotBeNullOrEmpty();
        envelope.Next.Single(call => call.Hint is not null && call.Hint.Contains("date=ge", StringComparison.Ordinal)).Args.ShouldNotContainKey("elements");
        NextCall count = envelope.Next.Single(call => call.Args.ContainsKey("summary"));
        count.Args["summary"].ShouldBe("count");
        count.Args["compartment"].ShouldBe("Patient/p1");
        envelope.Next.Single(call => call.Args.ContainsKey("maxBytes")).Args["maxBytes"].ShouldBe(EnvelopeJson.MaxBytes);
    }

    [Fact]
    public void GivenTheElementsCallFromNext_WhenSearched_ThenTheWholePageFitsTheDefaultBudget()
    {
        using TestFolder folder = Chart();
        var session = Open(folder);
        NextCall narrowed = ResourceSearcher.Search(session, Observations()).Next.Single(call => call.Args.ContainsKey("elements"));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(session, Request(narrowed.Args));

        envelope.Facts.ShouldNotBeNull().Returned.ShouldBe(ObservationCount);
        envelope.Omitted.ShouldBeEmpty();
        envelope.Next.ShouldBeEmpty();
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.DefaultBudget);
    }

    [Fact]
    public void GivenAPageCutOnlyByCountWithFewLeft_WhenSearched_ThenNextHasOnlyTheCallForTheRest()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Count: 2));

        envelope.Omitted.ShouldBe([new OmittedItem("3 more matches (offset=2)", "limit")]);
        NextCall rest = envelope.Next.ShouldHaveSingleItem();
        rest.Args.ShouldBe(new Dictionary<string, object> { ["resourceType"] = "Observation", ["count"] = 2, ["offset"] = 2 }, ignoreOrder: true);
        rest.Hint.ShouldBeNull();
    }

    [Fact]
    public void GivenMaxBytes_WhenSearched_ThenTheBudgetIsRaisedUpToTheCap()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Observations(maxBytes: EnvelopeJson.MaxBytes));

        envelope.Facts.ShouldNotBeNull().Returned.ShouldBe(ObservationCount);
        EnvelopeJson.SizeOf(envelope).ShouldBeGreaterThan(EnvelopeJson.DefaultBudget);
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
    }

    [Fact]
    public void GivenMoreThanTheCap_WhenSearched_ThenTheCapAppliesWithAWarning()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Observations(maxBytes: 500_000));

        envelope.Issues.ShouldContain(issue => issue.Code == "max-bytes-clamped" && issue.Severity == "warning");
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
    }

    [Fact]
    public void GivenATinyBudget_WhenSearched_ThenTheCallFailsWithInvalidParameter()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Observations(maxBytes: 100));

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public void GivenElements_WhenSearched_ThenMetaProfileIsDroppedButTheSubsettedTagStays()
    {
        using TestFolder folder = Chart();
        var session = Open(folder);

        JsonNode trimmed = Single(ResourceSearcher.Search(session, Observations(elements: "code", count: 1)));
        JsonNode whole = Single(ResourceSearcher.Search(session, Observations(count: 1)));

        trimmed["meta"]!.AsObject().ContainsKey("profile").ShouldBeFalse();
        ((string)trimmed["meta"]!["tag"]![0]!["code"]!).ShouldBe("SUBSETTED");
        ((string)whole["meta"]!["profile"]![0]!).ShouldBe(Profile);
    }

    [Fact]
    public void GivenResourcesFromOneFile_WhenSerialized_ThenTheFileIsNamedOnceAndEachItemPointsAtIt()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Observations(count: 3));
        JsonNode evidence = JsonNode.Parse(JsonSerializer.Serialize(envelope, EnvelopeJson.Compact))!["evidence"]!;

        evidence["files"]!.AsArray().Select(file => (string)file!).ShouldBe(["obs.ndjson"]);
        JsonArray items = evidence["items"]!.AsArray();
        items.Select(item => (string)item!["reference"]!).ShouldBe(["Observation/o00", "Observation/o01", "Observation/o02"]);
        items.Select(item => (int)item!["file"]!).ShouldAllBe(file => file == 0);
        items.Select(item => (int)item!["line"]!).ShouldBe([1, 2, 3]);
        items[0]!.AsObject().ContainsKey("offset").ShouldBeTrue();
    }

    [Fact]
    public void GivenResourcesFromTwoFilesAndOneNotOnALine_WhenSerialized_ThenEachFileIsListedOnceAndLineIsLeftOutWhereNone()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Observation/o00", "Patient/p1", "Observation/o01"]);
        JsonNode evidence = JsonNode.Parse(JsonSerializer.Serialize(envelope, EnvelopeJson.Compact))!["evidence"]!;

        evidence["files"]!.AsArray().Select(file => (string)file!).ShouldBe(["obs.ndjson", "patient.json"]);
        evidence["items"]!.AsArray().Select(item => (int)item!["file"]!).ShouldBe([0, 1, 0]);
        evidence["items"]![1]!.AsObject().ContainsKey("line").ShouldBeFalse();
    }

    [Fact]
    public void GivenReferencesLargerThanTheDefaultBudget_WhenRead_ThenTheResponseIsCutAndNextHasTheRemainingReferences()
    {
        using TestFolder folder = Chart();
        string[] references = [.. Enumerable.Range(0, ObservationCount).Select(i => $"Observation/o{i:00}")];

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), references);

        int returned = envelope.Facts.ShouldNotBeNull().Resources.Count;
        returned.ShouldBeInRange(1, ObservationCount - 1);
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.DefaultBudget);
        NextCall rest = envelope.Next.ShouldHaveSingleItem();
        rest.Tool.ShouldBe("read_resources");
        ((string[])rest.Args["references"]).ShouldBe(references.Skip(returned));
        rest.Hint.ShouldNotBeNull().ShouldContain("elements");
    }

    [Fact]
    public void GivenElementsAndMaxBytesOnAnExpandedNode_WhenThePageIsCut_ThenNextKeepsBoth()
    {
        using TestFolder folder = PatientFixture.Ndjson();

        ToolEnvelope<NodeFacts?> envelope = ContextExpander.Expand(PatientFixture.Open(folder), "p1", "labs/http://loinc.org|8867-4", count: 3, elements: "valueQuantity", maxBytes: 8192);

        NextCall rest = envelope.Next[0];
        rest.Args["offset"].ShouldBe(3);
        rest.Args["elements"].ShouldBe("valueQuantity");
        rest.Args["maxBytes"].ShouldBe(8192);
    }

    private static SearchRequest Request(IReadOnlyDictionary<string, object> args) => new(
        (string)args["resourceType"],
        args.TryGetValue("query", out object? query) ? (string)query : null,
        args.TryGetValue("count", out object? count) ? (int)count : null,
        args.TryGetValue("offset", out object? offset) ? (int)offset : 0,
        args.TryGetValue("summary", out object? summary) ? (string)summary : null,
        args.TryGetValue("elements", out object? elements) ? (string)elements : null,
        Lenient: false,
        args.TryGetValue("compartment", out object? compartment) ? (string)compartment : null,
        args.TryGetValue("maxBytes", out object? maxBytes) ? (int)maxBytes : null);
}
