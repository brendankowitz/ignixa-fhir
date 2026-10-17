// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.PatientFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The patient map, <c>patient_context</c> root's default and <c>context --compact</c>. New at V5: before it root returned only
/// the full digest, so every test here fails there (it does not compile).
/// </summary>
[Collection("Console")]
public class PatientMapTests
{
    private const string Snomed = "http://snomed.info/sct";

    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenAPatient_WhenMapped_ThenEveryDomainHasItsCountAndTopLines(string layout)
    {
        using TestFolder folder = Layout(layout);

        PatientMapFacts facts = PatientMap.Get(Open(folder), "p1", AsOf).Facts.ShouldNotBeNull();

        facts.Demographics.ShouldBe(new Demographics("Patient/p1", "Ann B Smith", "female", "1975-03-04", null));
        facts.Record.Resources.ShouldBe(32);
        facts.Record.From.ShouldBe("2010-01-01");
        facts.Record.To.ShouldBe("2025-06-01");
        facts.Domains.Keys.ShouldBe(PatientMap.DomainNames);
        facts.Domains["problems"].ShouldBe(new MapDomain(3, Active: 2, Resolved: 1, Last: "2020-05-01", Top: facts.Domains["problems"].Top));
        facts.Domains["problems"].Top.ShouldBe(["sct|195967001 Asthma @2020-05-01 Condition/c1", "sct|44054006 Diabetes mellitus type 2 @2018-01-01 Condition/c3"]);
        facts.Domains["medications"].Active.ShouldBe(4);
        facts.Domains["medications"].Top!.Select(line => line[..line.IndexOf(' ', StringComparison.Ordinal)]).ShouldBe(["(no", "rxnorm|1191", "rxnorm|435", "rxnorm|860975"]);
        facts.Domains["allergies"].ShouldBe(new MapDomain(0));
        facts.Domains["encounters"].ByClass.ShouldBe(new Dictionary<string, int> { ["AMB"] = 6 });
        facts.Domains["encounters"].Last.ShouldBe("2025-06-01");
        facts.Domains["labs"].Top.ShouldBe(["loinc|8867-4 Heart rate @2025-01-01 = 80 /min n=4 up Observation/o3", "loinc|2339-0 Glucose @2025-01-01 = 100 mg/dL n=3 down Observation/o9"]);
        facts.Domains["labs"].More.ShouldNotBeNull().ShouldContain("loinc|8480-6 Systolic blood pressure");
        facts.Domains["labs"].Excluded.ShouldBe(3);
        facts.Domains["immunizations"].ShouldBe(new MapDomain(0));
        facts.CodeSystems.Take(3).ShouldBe(["loinc=http://loinc.org", "v3-ActCode=http://terminology.hl7.org/CodeSystem/v3-ActCode", "rxnorm=http://www.nlm.nih.gov/research/umls/rxnorm"]);
        facts.CodeSystems.ShouldContain($"sct={Snomed}");
    }

    [Fact]
    public void GivenAMappedPatient_WhenItsLinesAreRead_ThenEachEndsWithItsResourceReferenceAndNoEvidenceIsListed()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Get(Open(folder), "p1");

        envelope.Evidence.ShouldBeEmpty();
        envelope.Next.ShouldBeEmpty();
        Dictionary<string, string> cited = envelope.Facts!.Domains.Values
            .SelectMany(domain => domain.Top ?? [])
            .ToDictionary(line => line, line => line[(line.LastIndexOf(' ') + 1)..]);
        cited["sct|195967001 Asthma @2020-05-01 Condition/c1"].ShouldBe("Condition/c1");
        cited.Single(pair => pair.Key.StartsWith("rxnorm|1191", StringComparison.Ordinal)).Value.ShouldBe("MedicationRequest/m5");
        cited.Single(pair => pair.Key.StartsWith("loinc|8867-4", StringComparison.Ordinal)).Value.ShouldBe("Observation/o3");
    }

    [Fact]
    public void GivenNoAllergiesImmunizationsOrGoals_WhenMapped_ThenAbsentNamesThemWithTheCompartmentChecked()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Get(Open(folder), "p1");

        AbsentItem missing = envelope.Absent.Single(item => item.Checked.StartsWith("none in compartment", StringComparison.Ordinal));
        missing.What.ShouldStartWith("AllergyIntolerance, Immunization, Procedure, CarePlan, CareTeam, DiagnosticReport, DocumentReference, FamilyMemberHistory, Goal");
        missing.Checked.ShouldBe("none in compartment; not a recorded negative");
        envelope.Absent.Select(item => item.What).ShouldContain("Medication/missing");
    }

    [Fact]
    public void GivenAPatientWithAllergies_WhenMapped_ThenAllergiesAreListedAndNotAbsent()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Get(Open(folder), "p2");

        envelope.Facts!.Domains["allergies"].Top.ShouldBe(["sct|91935009 Peanut @2021-02-02 AllergyIntolerance/a1"]);
        envelope.Absent.ShouldAllBe(item => !item.What.Contains("AllergyIntolerance", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenOneTestUnderTwoCodesAndOrdersByReference_WhenMapped_ThenQuirksNameBoth()
    {
        using TestFolder folder = Ndjson().Write("potassium.ndjson", string.Join('\n',
            Potassium("k1", "6298-4", "Potassium [Moles/volume] in Blood", "2024-01-01", 4.1m),
            Potassium("k2", "2823-3", "Potassium [Moles/volume] in Serum or Plasma", "2024-06-01", 4.4m),
            Potassium("k3", "2823-3", "Potassium [Moles/volume] in Serum or Plasma", "2025-02-01", 4.6m)));

        PatientMapFacts facts = PatientMap.Get(Open(folder), "p1").Facts.ShouldNotBeNull();

        facts.Quirks.ShouldNotBeNull().ShouldContain("Potassium: loinc|2823-3 n=2, loinc|6298-4 n=1; search all");
        facts.Quirks.ShouldContain("2/5 medication orders use medicationReference (read the Medication too)");
    }

    [Fact]
    public void GivenAHostileCodeSystem_WhenMapped_ThenItIsShownOnlyAsADigestAndNoFreeTextIsCopied()
    {
        using TestFolder folder = Ndjson();

        string json = JsonSerializer.Serialize(PatientMap.Get(Open(folder), "p1"), EnvelopeJson.Compact);

        json.ShouldNotContain(HostileSystem);
        json.ShouldNotContain(Hostile);
        json.ShouldNotContain("free text");
    }

    [Fact]
    public void GivenTheFixtureAndALargeRecord_WhenMapped_ThenTheResponseStaysWithinItsBudget()
    {
        using TestFolder small = Ndjson();
        using TestFolder large = LargeRecord();

        int smallBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(PatientMap.Get(Open(small), "p1"), EnvelopeJson.Compact));
        int largeBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(PatientMap.Get(Open(large), "big"), EnvelopeJson.Compact));
        int fullBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(ContextRoot.Get(Open(large), "big"), EnvelopeJson.Compact));

        smallBytes.ShouldBeLessThan(3072);
        largeBytes.ShouldBeLessThan(4608);
        largeBytes.ShouldBeLessThan(fullBytes / 2);
    }

    [Fact]
    public void GivenRootWithoutDetail_WhenCalledOverMcp_ThenItIsTheMapAndDetailFullIsTheDigest()
    {
        using TestFolder folder = Ndjson();
        FolderSession session = Open(folder);
        var tools = new PatientTools(session);

        JsonNode map = Envelope(tools.PatientContext("root", "p1"));
        JsonNode explicitMap = Envelope(tools.PatientContext("root", "p1", detail: "map"));
        JsonNode full = Envelope(tools.PatientContext("root", "p1", detail: "full"));

        map["facts"]!["domains"].ShouldNotBeNull();
        explicitMap.ToJsonString().ShouldBe(map.ToJsonString());
        full.ToJsonString().ShouldBe(WithoutEmptyMembers(JsonNode.Parse(JsonSerializer.Serialize(ContextRoot.Get(session, "p1"), EnvelopeJson.Compact))!).ToJsonString());
    }

    [Fact]
    public void GivenAnUnknownDetail_WhenRootIsCalled_ThenItFailsNamingTheChoices()
    {
        using TestFolder folder = Ndjson();

        CallToolResult result = new PatientTools(Open(folder)).PatientContext("root", "p1", detail: "everything");

        result.IsError.ShouldBe(true);
        ((string)Envelope(result)["issues"]![0]!["diagnostics"]!).ShouldBe("'detail' must be 'map' (the default) or 'full', not 'everything'.");
    }

    [Fact]
    public async Task GivenTheSameRecord_WhenMappedOnTheCliWithCompactAndOverMcp_ThenTheResponsesMatch()
    {
        using TestFolder folder = PastOnlyRecord();
        JsonNode overMcp = Envelope(new PatientTools(Open(folder)).PatientContext("root", "p1"));

        (int exitCode, JsonNode cli) = await RunAsync("context", folder.Path, "p1", "--compact", "--no-cache");

        exitCode.ShouldBe(0);
        WithoutClock(cli).ToJsonString().ShouldBe(WithoutClock(overMcp).ToJsonString());
    }

    [Fact]
    public void GivenCompactWithANode_WhenTheContextVerbRuns_ThenItIsRejected()
    {
        using TestFolder folder = Ndjson();
        var root = new RootCommand();
        root.Subcommands.Add(ContextCommand.Create());

        ParseResult parsed = root.Parse(["context", folder.Path, "p1", "--compact", "--node", "labs", "--no-cache"]);

        parsed.Errors.ShouldHaveSingleItem().Message.ShouldBe("--compact prints the patient map, the root; it does not apply with --node.");
    }

    [Fact]
    public void GivenAnUnknownPatient_WhenMapped_ThenTheCallFailsAsTheDigestDoes()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Get(Open(folder), "nobody");

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("unknown-patient");
    }

    private static string Potassium(string id, string code, string display, string date, decimal value) =>
        $$$"""{"resourceType":"Observation","id":"{{{id}}}","status":"final","category":[{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/observation-category","code":"laboratory"}]}],"code":{"coding":[{"system":"http://loinc.org","code":"{{{code}}}","display":"{{{display}}}"}]},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"{{{date}}}","valueQuantity":{"value":{{{value}}},"unit":"mmol/L"}}""";

    /// <summary>One patient with many problems, medications, encounters, lab codes, immunizations and procedures.</summary>
    private static TestFolder LargeRecord()
    {
        List<string> lines = ["""{"resourceType":"Patient","id":"big","name":[{"family":"Large","given":["Rec"]}],"gender":"male","birthDate":"1950-01-01"}"""];
        for (int i = 0; i < 40; i++)
        {
            string day = $"20{10 + (i % 15):00}-0{1 + (i % 9)}-1{i % 10}";
            lines.Add($$$"""{"resourceType":"Condition","id":"c{{{i}}}","clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"{{{(i % 3 == 0 ? "resolved" : "active")}}}"}]},"code":{"coding":[{"system":"{{{Snomed}}}","code":"{{{100000 + i}}}","display":"A fairly long problem display number {{{i}}} (disorder)"}]},"subject":{"reference":"Patient/big"},"onsetDateTime":"{{{day}}}"}""");
            lines.Add($$$"""{"resourceType":"MedicationRequest","id":"m{{{i}}}","status":"{{{(i % 2 == 0 ? "active" : "stopped")}}}","intent":"order","subject":{"reference":"Patient/big"},"medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"{{{200000 + i}}}","display":"Drug number {{{i}}} 10 MG Oral Tablet with a long name"}]},"authoredOn":"{{{day}}}"}""");
            lines.Add($$$"""{"resourceType":"Encounter","id":"e{{{i}}}","status":"finished","class":{"code":"{{{(i % 4 == 0 ? "EMER" : "AMB")}}}"},"type":[{"coding":[{"system":"{{{Snomed}}}","code":"185349003","display":"Encounter for check up (procedure)"}]}],"subject":{"reference":"Patient/big"},"period":{"start":"{{{day}}}"}}""");
            lines.Add($$$"""{"resourceType":"Observation","id":"o{{{i}}}","status":"final","category":[{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/observation-category","code":"laboratory"}]}],"code":{"coding":[{"system":"http://loinc.org","code":"{{{1000 + i}}}-1","display":"Analyte {{{i}}} [Mass/volume] in Serum or Plasma"}]},"subject":{"reference":"Patient/big"},"effectiveDateTime":"{{{day}}}","valueQuantity":{"value":{{{i}}},"unit":"mg/dL"}}""");
            lines.Add($$$"""{"resourceType":"Immunization","id":"i{{{i}}}","status":"completed","vaccineCode":{"coding":[{"system":"http://hl7.org/fhir/sid/cvx","code":"{{{i % 6}}}","display":"Vaccine {{{i % 6}}}"}]},"patient":{"reference":"Patient/big"},"occurrenceDateTime":"{{{day}}}"}""");
            lines.Add($$$"""{"resourceType":"Procedure","id":"x{{{i}}}","status":"completed","code":{"coding":[{"system":"{{{Snomed}}}","code":"{{{300000 + (i % 7)}}}","display":"Procedure kind {{{i % 7}}} (procedure)"}]},"subject":{"reference":"Patient/big"},"performedDateTime":"{{{day}}}"}""");
        }

        return new TestFolder().Write("big.ndjson", string.Join('\n', lines));
    }

    /// <summary>A record with no date after today, so a session on the system clock and one on the fixture clock agree.</summary>
    private static TestFolder PastOnlyRecord() => new TestFolder().Write("past.ndjson", string.Join('\n', Resources.Where(resource =>
        !resource.Contains("\"id\":\"o10\"", StringComparison.Ordinal) && !resource.Contains("\"id\":\"e8\"", StringComparison.Ordinal))));

    private static JsonNode Envelope(CallToolResult result) => JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;

    /// <summary>The envelope without what follows each session's clock: the freshness instant.</summary>
    private static JsonNode WithoutClock(JsonNode envelope)
    {
        JsonNode copy = WithoutEmptyMembers(envelope);
        copy.AsObject().Remove("freshness");
        foreach (JsonNode? item in copy["absent"]?.AsArray() ?? [])
        {
            item!.AsObject().Remove("indexBuiltAt");
        }

        return copy;
    }

    /// <summary>The envelope as the MCP tool sends it: without derived, an empty absent and an unchanged folder's freshness.</summary>
    private static JsonNode WithoutEmptyMembers(JsonNode envelope)
    {
        JsonNode copy = envelope.DeepClone();
        copy.AsObject().Remove("derived");
        if (copy["absent"] is JsonArray { Count: 0 })
        {
            copy.AsObject().Remove("absent");
        }

        if ((int?)copy["freshness"]?["staleFiles"] == 0)
        {
            copy.AsObject().Remove("freshness");
        }

        return copy;
    }

    private static async Task<(int ExitCode, JsonNode Output)> RunAsync(params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(ContextCommand.Create());
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
