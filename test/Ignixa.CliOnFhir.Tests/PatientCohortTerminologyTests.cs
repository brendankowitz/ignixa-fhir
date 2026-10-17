// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Ignixa.CliOnFhir.Cohorts;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.DrugClasses;
using Ignixa.CliOnFhir.Terminology.Remote;
using Ignixa.CliOnFhir.Terminology.Tools;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.CohortFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// patient_cohort criteria whose codes come from terminology: a Condition ValueSet expanded as terminology expand scope=folder
/// does (loaded content, or the tx server for isa/&lt;sctid&gt;), and a drug class from RxClass (a stub server, never the
/// network). New capability (V10), as for <see cref="PatientCohortTests"/>.
/// </summary>
public class PatientCohortTerminologyTests
{
    private static readonly Uri TxServer = new("https://tx.example.org/r4");
    private static readonly Uri RxClassServer = new("https://rxnav.example.org/REST");

    private static readonly Dictionary<string, string> RxClassAnswers = new(StringComparer.Ordinal)
    {
        ["classMembers.json?classId=C10AA&relaSource=ATCPROD"] = Members(("617310", "atorvastatin 20 MG Oral Tablet", "SCD"), ("312961", "Simvastatin 20 MG Oral Tablet", "SCD"), ("859747", "rosuvastatin calcium 10 MG Oral Tablet", "SCD")),
        ["classMembers.json?classId=C10AA&relaSource=ATC"] = Members(("83367", "atorvastatin", "IN")),
    };

    private static string Members(params (string Rxcui, string Name, string Tty)[] members) =>
        new JsonObject { ["drugMemberGroup"] = new JsonObject { ["drugMember"] = new JsonArray([.. members.Select(item => (JsonNode)new JsonObject { ["minConcept"] = new JsonObject { ["rxcui"] = item.Rxcui, ["name"] = item.Name, ["tty"] = item.Tty } })]) } }.ToJsonString();

    private static HttpResponseMessage Json(string json, string mediaType) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

    /// <summary>Statins for p1 through a Medication (617310) and p2 inline (312961); p3 takes lisinopril.</summary>
    private static TestFolder Orders() => new TestFolder()
        .Write("patients.ndjson", Lines(Patient("p1", "1970-01-01"), Patient("p2", "1970-01-01"), Patient("p3", "1970-01-01")))
        .Write("orders.ndjson", Lines(
            OrderByReference("m1", "p1", "med1"),
            Medication("med1", "617310"),
            Order("m2", "p2", "312961"),
            Order("m3", "p3", "314076")));

#pragma warning disable CA2000 // Each client owns its stub handler and lives as long as its session.
    private static (FolderSession Session, StubHttpHandler RxClass) WithRxClass(TestFolder folder)
    {
        var handler = new StubHttpHandler(request =>
        {
            string key = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery[(RxClassServer.AbsolutePath.Length + "/rxclass/".Length)..]);
            return Json(RxClassAnswers.TryGetValue(key, out string? json) ? json : "{}", "application/json");
        });
        var client = new RxClassClient(RxClassServer, cacheDirectory: null, handler, RxClassClient.DefaultTimeout, new ManualTimeProvider());
        return (TerminologyFixture.Open(folder, new TerminologyOptions { RxClass = client }), handler);
    }

    private static (FolderSession Session, StubHttpHandler Tx) WithTxServer(TestFolder folder, TestFolder store, params string[] codes)
    {
        string contains = string.Join(',', codes.Select(code => $$"""{"system":"{{Sct}}","code":"{{code}}"}"""));
        var tx = new StubHttpHandler(_ => Json($$$"""{"resourceType":"ValueSet","expansion":{"total":{{{codes.Length}}},"contains":[{{{contains}}}]}}""", "application/fhir+json"));
        var options = TerminologyFixture.Online(TerminologyFixture.Registry(TerminologyFixture.Archive(TerminologyFixture.Files())), store) with { TxServer = new TxServerClient(TxServer, tx, TxServerClient.DefaultTimeout) };
        return (TerminologyFixture.Open(folder, options), tx);
    }
#pragma warning restore CA2000

    [Fact]
    public async Task GivenADrugClass_WhenCounted_ThenItsMembersTheFolderUsesAreMatchedInBothMedicationShapesAndListedInTheStep()
    {
        using TestFolder folder = Orders();
        (FolderSession session, StubHttpHandler rxClass) = WithRxClass(folder);

        ToolEnvelope<CohortFacts?> envelope = await Count(session, new CohortArguments(Medications: CohortFixture.Json("""[{"classId":"C10AA"}]""")));

        CohortFacts facts = Facts(envelope);
        facts.Patients.ShouldBe(["Patient/p1", "Patient/p2"]);
        CohortStep step = facts.Steps[^1];
        step.Codes.ShouldBe([$"{RxNorm}|312961", $"{RxNorm}|617310"]);
        step.CodeCount.ShouldBe(2);
        envelope.Evidence.Select(item => item.Reference).ShouldBe(["Patient/p1", "MedicationRequest/m1", "Patient/p2", "MedicationRequest/m2"]);
        envelope.Issues.ShouldContain(issue => issue.Code == RxClassReads.AnsweredCode);
        rxClass.Requests.ShouldAllBe(uri => uri.AbsolutePath.StartsWith("/REST/rxclass/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenAClassWithoutRxClass_WhenCounted_ThenTheCallFailsNamingTheServerOption()
    {
        using TestFolder folder = Orders();

        IssueItem issue = Failure(await Count(SearchFixture.Open(folder), new CohortArguments(Medications: CohortFixture.Json("""{"classId":"C10AA"}"""))));

        issue.Code.ShouldBe(DrugClassCall.NotEnabledCode);
        issue.Diagnostics.ShouldContain("--rxclass-server");
    }

    [Fact]
    public async Task GivenAnIngredientOnlyClassSource_WhenCounted_ThenTheWarningsSayWhyNothingMatched()
    {
        using TestFolder folder = Orders();
        (FolderSession session, _) = WithRxClass(folder);

        ToolEnvelope<CohortFacts?> envelope = await Count(session, new CohortArguments(Medications: CohortFixture.Json("""{"classId":"N0000175589","source":"EPC"}""")));

        Facts(envelope).Count.ShouldBe(0);
        envelope.Issues.Select(issue => issue.Code).ShouldBe([RxClassReads.AnsweredCode, TermClassMembers.EmptyClassCode, TermClassMembers.IngredientsOnlyCode, "cohort-empty-step"], ignoreOrder: true);
        envelope.Issues.Single(issue => issue.Code == "cohort-empty-step").Diagnostics.ShouldBe("medications[0] is the first step that left no patient: the criterion has no code to search: its valueSet or class lists none that the folder uses.");
    }

    [Fact]
    public async Task GivenALoadedValueSet_WhenAConditionCriterionNamesIt_ThenTheCodesTheFolderUsesAreSearchedWithTheListedOnes()
    {
        using TestFolder folder = new TestFolder()
            .Write("patients.ndjson", Lines(Patient("p1", "1970-01-01"), Patient("p2", "1970-01-01"), Patient("p3", "1970-01-01"), Patient("p4", "1970-01-01")))
            .Write("conditions.ndjson", Lines(
                Condition("c1", "p1", "mi", system: TerminologyFixture.Body),
                Condition("c2", "p2", "heart", system: TerminologyFixture.Body),
                Condition("c3", "p3", "device", system: TerminologyFixture.Body),
                Condition("c4", "p4", "old", system: TerminologyFixture.Body)));
        using var store = new TestFolder();
        string criterion = $$"""{"valueSet":"{{TerminologyFixture.ValueSetBase}}current-disorders","codes":["{{TerminologyFixture.Body}}|device"]}""";

        CohortFacts facts = Facts(await Count(TerminologyFixture.OpenWithPackage(folder, store), new CohortArguments(Conditions: CohortFixture.Json(criterion))));

        facts.Patients.ShouldBe(["Patient/p1", "Patient/p2", "Patient/p3"]);
        CohortStep step = facts.Steps[^1];
        step.Codes.ShouldBe([$"{TerminologyFixture.Body}|heart", $"{TerminologyFixture.Body}|mi"]);
        step.CodeCount.ShouldBe(3);
    }

    [Fact]
    public async Task GivenAnIsAShorthand_WhenCountedWithATerminologyServer_ThenTheServersExpansionDecidesWhichFolderCodesCount()
    {
        using TestFolder folder = new TestFolder()
            .Write("patients.ndjson", Lines(Patient("p1", "1970-01-01"), Patient("p2", "1970-01-01")))
            .Write("conditions.ndjson", Lines(Condition("c1", "p1", "401314000"), Condition("c2", "p2", "22298006")));
        using var store = new TestFolder();
        (FolderSession session, StubHttpHandler tx) = WithTxServer(folder, store, "414545008", "401314000");

        ToolEnvelope<CohortFacts?> envelope = await Count(session, new CohortArguments(Conditions: CohortFixture.Json("""{"valueSet":"isa/414545008"}""")));

        Facts(envelope).Patients.ShouldBe(["Patient/p1"]);
        Facts(envelope).Steps[^1].Codes.ShouldBe([$"{Sct}|401314000"]);
        tx.Requests.Select(uri => HttpUtility.ParseQueryString(uri.Query)["url"]).ShouldContain(TermExpand.SnomedIsAPrefix + "414545008");
    }

    [Fact]
    public async Task GivenAValueSetNotLoadedAndNoTerminologyServer_WhenCounted_ThenTheCallFailsNamingTheServerOption()
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", Patient("p1", "1970-01-01"));
        using var store = new TestFolder();

        IssueItem issue = Failure(await Count(TerminologyFixture.OpenWithPackage(folder, store), new CohortArguments(Conditions: CohortFixture.Json("""{"valueSet":"isa/44054006"}"""))));

        issue.Code.ShouldBe(TermExpand.ValueSetNotLoaded);
        issue.Diagnostics.ShouldContain("--tx-server");
    }
}
