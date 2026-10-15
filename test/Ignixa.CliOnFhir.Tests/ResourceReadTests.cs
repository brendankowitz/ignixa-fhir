// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

public class ResourceReadTests
{
    [Fact]
    public void GivenKnownReferences_WhenRead_ThenResourcesComeBackInRequestOrderWithEvidence()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Observation/o4", "Patient/p1"]);

        ReadFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Resources.Select(resource => (string)resource["id"]!).ShouldBe(["o4", "p1"]);
        envelope.Evidence.Select(item => (item.Reference, item.File, item.Line)).ShouldBe(
            [("Observation/o4", ObservationsFile, 4), ("Patient/p1", PatientsFile, 1)]);
        envelope.Absent.ShouldBeEmpty();
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenResourceInASingleFile_WhenRead_ThenEvidenceHasNoLineAndOffsetZero()
    {
        using TestFolder folder = new TestFolder().Write("a/p.json", """{"resourceType":"Patient","id":"p1"}""");

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Patient/p1"]);

        envelope.Evidence.ShouldHaveSingleItem().ShouldBe(new EvidenceItem("Patient/p1", "a/p.json", Line: null, Offset: 0));
    }

    [Fact]
    public void GivenResourceInsideABundle_WhenRead_ThenOnlyThatEntryIsReturnedWithItsOffset()
    {
        const string Bundle = """{"resourceType":"Bundle","type":"collection","entry":[{"resource":{"resourceType":"Patient","id":"b1","active":true}},{"resource":{"resourceType":"Patient","id":"b2","active":false}}]}""";
        using TestFolder folder = new TestFolder().Write("bundle.json", Bundle);

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Patient/b2"]);

        JsonNode patient = envelope.Facts.ShouldNotBeNull().Resources.ShouldHaveSingleItem();
        ((bool)patient["active"]!).ShouldBeFalse();
        envelope.Evidence.ShouldHaveSingleItem().Offset.ShouldBe(Bundle.IndexOf("{\"resourceType\":\"Patient\",\"id\":\"b2\"", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenMissingReference_WhenRead_ThenItIsAbsentWithWhatWasChecked()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Patient/p1", "Patient/nobody", "Condition/c1"]);

        envelope.Facts.ShouldNotBeNull().Resources.ShouldHaveSingleItem();
        envelope.Absent.Select(item => item.What).ShouldBe(["Patient/nobody", "Condition/c1"]);
        AbsentItem patient = envelope.Absent[0];
        patient.Checked.ShouldContain("3 Patient resources");
        patient.Checked.ShouldContain("2 files");
        ((DateTimeOffset?)patient.IndexBuiltAt).ShouldBe(envelope.Freshness.IndexBuiltAt);
        envelope.Absent[1].Checked.ShouldContain("0 Condition resources");
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenDuplicateReferences_WhenRead_ThenEachResourceIsReturnedOnce()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Patient/p1", "Patient/p1"]);

        envelope.Facts.ShouldNotBeNull().Requested.ShouldBe(1);
        envelope.Facts.Resources.Count.ShouldBe(1);
        envelope.Evidence.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenElements_WhenRead_ThenResourcesAreTrimmedAndTaggedSubsetted()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Patient/p1", "Observation/o1"], elements: "gender,valueQuantity");

        JsonObject patient = envelope.Facts.ShouldNotBeNull().Resources[0].AsObject();
        patient.Select(property => property.Key).ShouldBe(["resourceType", "id", "gender", "meta"], ignoreOrder: true);
        ((string)patient["meta"]!["tag"]![0]!["code"]!).ShouldBe("SUBSETTED");
        envelope.Facts.Resources[1].AsObject().Select(property => property.Key).ShouldBe(["resourceType", "id", "valueQuantity", "meta"], ignoreOrder: true);
    }

    [Fact]
    public void GivenElementThatDoesNotExistOnAType_WhenRead_ThenOneWarningPerTypeNamesIt()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Patient/p1", "Observation/o1"], elements: "gender");

        IssueItem warning = envelope.Issues.ShouldHaveSingleItem();
        warning.Code.ShouldBe("element-ignored");
        warning.Diagnostics.ShouldContain("Observation");
    }

    [Theory]
    [InlineData("nonsense", "invalid-reference")]
    [InlineData("Patient/", "invalid-reference")]
    [InlineData("Patient/p1/_history/1", "invalid-reference")]
    [InlineData("http://example.org/Patient/p1", "invalid-reference")]
    [InlineData("Pateint/p1", "unknown-resource-type")]
    public void GivenMalformedReference_WhenRead_ThenTheCallFails(string reference, string code)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Patient/p1", reference]);

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe(code);
        envelope.Evidence.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNoReferences_WhenRead_ThenTheCallFails()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), []);

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public void GivenMoreReferencesThanTheLimit_WhenRead_ThenTheRestAreOmittedNotSilentlyDropped()
    {
        using TestFolder folder = Clinic();
        string[] references = [.. Enumerable.Range(0, ResourceFetcher.MaxReferences + 3).Select(i => $"Patient/x{i}")];

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), references);

        envelope.Absent.Count.ShouldBe(ResourceFetcher.MaxReferences);
        envelope.Omitted.ShouldHaveSingleItem().What.ShouldStartWith("3 more references");
        envelope.Omitted[0].Why.ShouldBe("limit");
    }

    [Fact]
    public void GivenFoundResourcesLargerThanTheBudget_WhenRead_ThenTheRestAreOmittedByReference()
    {
        string padding = new('x', 20_000);
        using TestFolder folder = new TestFolder().Write(
            "p.ndjson",
            string.Join('\n', Enumerable.Range(0, 4).Select(i => "{\"resourceType\":\"Patient\",\"id\":\"p" + i + "\",\"text\":{\"status\":\"generated\",\"div\":\"" + padding + "\"}}")));

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Patient/p0", "Patient/p1", "Patient/p2", "Patient/p3"]);

        envelope.Facts.ShouldNotBeNull().Resources.Count.ShouldBe(2);
        OmittedItem omitted = envelope.Omitted.ShouldHaveSingleItem();
        omitted.Why.ShouldBe("size-budget");
        omitted.What.ShouldStartWith("2 found resources not returned, from Patient/p2 on");
    }

    [Fact]
    public void GivenFileChangedSinceCatalog_WhenRead_ThenTheStaleResourceIsReportedNotReturned()
    {
        using TestFolder folder = Clinic();
        FolderSession session = Open(folder);
        session.Catalog.ShouldNotBeNull();
        folder.Write(PatientsFile, File.ReadAllText(Path.Combine(folder.Path, PatientsFile)).Replace("\"id\":\"p1\"", "\"id\":\"zz\"", StringComparison.Ordinal));

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(session, ["Patient/p1", "Patient/p2"]);

        envelope.Facts.ShouldNotBeNull().Resources.Select(resource => (string)resource["id"]!).ShouldBe(["p2"]);
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("catalog-stale");
    }

    [Fact]
    public void GivenFileDeletedSinceCatalog_WhenRead_ThenTheResourceIsReportedUnreadable()
    {
        using TestFolder folder = Clinic();
        FolderSession session = Open(folder);
        session.Catalog.ShouldNotBeNull();
        File.Delete(Path.Combine(folder.Path, PatientsFile));

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(session, ["Patient/p1"]);

        envelope.Facts.ShouldNotBeNull().Resources.ShouldBeEmpty();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("resource-unreadable");
    }

    [Fact]
    public void GivenDecimalValue_WhenRead_ThenItsLexicalFormSurvives()
    {
        using TestFolder folder = new TestFolder().Write(
            "o.json",
            """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"valueQuantity":{"value":1.50,"unit":"kg"}}""");

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Observation/o1"], elements: "valueQuantity");

        envelope.Facts.ShouldNotBeNull().Resources.ShouldHaveSingleItem().ToJsonString().ShouldContain("\"value\":1.50");
    }

    [Fact]
    public void GivenMcpTools_WhenCalled_ThenTheyAnswerLikeTheUnderlyingOperations()
    {
        using TestFolder folder = Clinic();
        var tools = new ResourceTools(Open(folder));

        JsonNode search = Envelope(tools.SearchFhirResources("Patient", "family=Jones", count: 5), expectError: false);
        JsonNode read = Envelope(tools.ReadResources(["Patient/p3", "Patient/none"]), expectError: false);

        search["facts"]!["resources"]!.AsArray().Select(resource => (string)resource!["id"]!).ShouldBe(["p2"]);
        read["facts"]!["resources"]!.AsArray().ShouldHaveSingleItem();
        ((string)read["absent"]![0]!["what"]!).ShouldBe("Patient/none");
    }

    [Fact]
    public void GivenRequestThatCannotBeAnswered_WhenMcpToolCalled_ThenTheResultIsFlaggedAsErrorAndStillCarriesTheEnvelope()
    {
        using TestFolder folder = Clinic();
        var tools = new ResourceTools(Open(folder));

        JsonNode search = Envelope(tools.SearchFhirResources("Patient", "_text=Smith"), expectError: true);
        JsonNode read = Envelope(tools.ReadResources(["nonsense"]), expectError: true);

        search["facts"].ShouldBeNull();
        ((string)search["issues"]![0]!["code"]!).ShouldBe("search-not-supported");
        ((string)read["issues"]![0]!["code"]!).ShouldBe("invalid-reference");
    }

    private static JsonNode Envelope(CallToolResult result, bool expectError)
    {
        (result.IsError ?? false).ShouldBe(expectError);
        return JsonNode.Parse(result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text)!;
    }

    [Theory]
    [InlineData(nameof(ResourceTools.SearchFhirResources), "search_fhir_resources")]
    [InlineData(nameof(ResourceTools.ReadResources), "read_resources")]
    public void GivenMcpToolMethods_WhenInspected_ThenTheyAreNamedReadOnlyAndIdempotent(string method, string name)
    {
        McpServerToolAttribute attribute = typeof(ResourceTools).GetMethod(method)!.GetCustomAttribute<McpServerToolAttribute>().ShouldNotBeNull();

        attribute.Name.ShouldBe(name);
        attribute.ReadOnly.ShouldBeTrue();
        attribute.Idempotent.ShouldBeTrue();
    }
}
