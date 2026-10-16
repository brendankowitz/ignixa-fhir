// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.TerminologyFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// folder_code_systems with text (V11): the codes the folder holds whose display matches some words, so an agent can turn a
/// category ("any diabetes", "old MI") into the codes the data uses. New capability: at e8a3177b the tool took no text.
/// </summary>
[Collection("Console")]
public class FolderCodeTextTests
{
    private const string Icd10Cm = "http://hl7.org/fhir/sid/icd-10-cm";
    private const string ClinicalStatus = "http://terminology.hl7.org/CodeSystem/condition-clinical";

    private static string Condition(string id, string patient, string system, string code, string display, string status) =>
        $$$"""{"resourceType":"Condition","id":"{{{id}}}","clinicalStatus":{"coding":[{"system":"{{{ClinicalStatus}}}","code":"{{{status}}}"}]},"subject":{"reference":"Patient/{{{patient}}}"},"code":{"coding":[{"system":"{{{system}}}","code":"{{{code}}}","display":"{{{display}}}"}]}}""";

    private static readonly string[] Resources =
    [
        """{"resourceType":"Patient","id":"p1"}""",
        """{"resourceType":"Patient","id":"p2"}""",
        """{"resourceType":"Patient","id":"p3"}""",
        Condition("c1", "p1", Snomed, "44054006", "Diabetes mellitus type 2 (disorder)", "active"),
        Condition("c2", "p1", Snomed, "127013003", "Disorder of kidney due to diabetes mellitus (disorder)", "active"),
        Condition("c3", "p2", Snomed, "44054006", "Diabetes mellitus type 2 (disorder)", "active"),
        Condition("c4", "p2", Snomed, "22298006", "Myocardial infarction (disorder)", "resolved"),
        Condition("c5", "p3", Icd10Cm, "E11.9", "Type 2 diabetes mellitus without complications", "active"),
        Condition("c6", "p3", Snomed, "399211009", "History of myocardial infarction (situation)", "active"),
        $$$"""{"resourceType":"Procedure","id":"pr1","status":"completed","subject":{"reference":"Patient/p3"},"code":{"coding":[{"system":"{{{Snomed}}}","code":"385804009","display":"Diabetic education (procedure)"}]}}""",
        """{"resourceType":"Observation","id":"o1","status":"final","subject":{"reference":"Patient/p1"},"code":{"coding":[{"system":"http://loinc.org","code":"4548-4","display":"Hemoglobin A1c/Hemoglobin.total in Blood"}]}}""",
    ];

    private static TestFolder Folder(IEnumerable<string>? resources = null) =>
        new TestFolder().Write("data.ndjson", string.Join('\n', resources ?? Resources) + "\n");

    private static FolderSession Plain(TestFolder folder) => new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);

    private static async Task<JsonNode> Search(FolderSession session, string text, string? resourceType = null, string? system = null, int? count = null, int offset = 0) =>
        Json(await new FolderTools(session).FolderCodeSystems(count, offset, text: text, resourceType: resourceType, system: system));

    private static string[] Codes(JsonNode result) =>
        [.. result["facts"]!["codes"]!.AsArray().Select(code => (string)code!["code"]!)];

    private static JsonNode Row(JsonNode result, string code) =>
        result["facts"]!["codes"]!.AsArray().Single(row => (string)row!["code"]! == code)!;

    [Fact]
    public async Task GivenAWord_WhenSearched_ThenCodesWithADisplayWordStartingWithItMatchWhateverTheCase()
    {
        using TestFolder folder = Folder();

        JsonNode result = await Search(Plain(folder), "DIABET");

        Codes(result).ShouldBe(["sct|44054006", "icd10cm|E11.9", "sct|127013003", "sct|385804009"], ignoreOrder: true);
        ((string)Row(result, "sct|127013003")["display"]!).ShouldBe("Disorder of kidney due to diabetes mellitus (disorder)");
        ((string)result["facts"]!["systems"]!["sct"]!).ShouldBe(Snomed);
        ((string)result["facts"]!["systems"]!["icd10cm"]!).ShouldBe(Icd10Cm);
    }

    [Fact]
    public async Task GivenTheMiddleOfAWord_WhenSearched_ThenItDoesNotMatch()
    {
        using TestFolder folder = Folder();

        JsonNode result = await Search(Plain(folder), "betes");

        Codes(result).ShouldBeEmpty();
        result["absent"]!.AsArray().Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("diabet|myocard")]
    [InlineData("diabet,myocard")]
    [InlineData("diabet myocard")]
    public async Task GivenAlternatives_WhenSearched_ThenACodeMatchingAnyWordIsListed(string text)
    {
        using TestFolder folder = Folder();

        JsonNode result = await Search(Plain(folder), text);

        Codes(result).ShouldBe(
            ["sct|44054006", "icd10cm|E11.9", "sct|127013003", "sct|385804009", "sct|22298006", "sct|399211009"],
            ignoreOrder: true);
        Codes(result).ShouldNotContain("loinc|4548-4");
    }

    [Fact]
    public async Task GivenAResourceType_WhenSearched_ThenOnlyCodesOnThatTypeAreListed()
    {
        using TestFolder folder = Folder();

        JsonNode result = await Search(Plain(folder), "diabet", resourceType: "Condition");

        Codes(result).ShouldNotContain("sct|385804009");
        Codes(result).Length.ShouldBe(3);
    }

    [Fact]
    public async Task GivenASystemAlias_WhenSearched_ThenOnlyCodesOfThatSystemAreListed()
    {
        using TestFolder folder = Folder();

        JsonNode result = await Search(Plain(folder), "diabet", system: "icd10cm");

        Codes(result).ShouldBe(["icd10cm|E11.9"]);
    }

    [Fact]
    public async Task GivenAResourceTypeTheVersionDoesNotHave_WhenSearched_ThenTheCallFails()
    {
        using TestFolder folder = Folder();

        JsonNode result = await Search(Plain(folder), "diabet", resourceType: "Diagnosis");

        ((string)result["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
        result["facts"].ShouldBeNull();
    }

    [Fact]
    public async Task GivenARepeatedCode_WhenSearched_ThenTheCountsAreResourcesPatientsAndActiveConditions()
    {
        using TestFolder folder = Folder();

        JsonNode result = await Search(Plain(folder), "diabet|myocard");

        JsonNode diabetes = Row(result, "sct|44054006");
        ((int)diabetes["resources"]!, (int)diabetes["patients"]!, (int)diabetes["active"]!).ShouldBe((2, 2, 2));
        diabetes["types"]!.AsArray().Select(type => (string)type!).ShouldBe(["Condition:2"]);
        JsonNode resolved = Row(result, "sct|22298006");
        ((int)resolved["resources"]!, (int)resolved["patients"]!, (int)resolved["active"]!).ShouldBe((1, 1, 0));
        JsonNode education = Row(result, "sct|385804009");
        education["types"]!.AsArray().Select(type => (string)type!).ShouldBe(["Procedure:1"]);
        education["active"].ShouldBeNull();
    }

    [Fact]
    public async Task GivenSeveralConditionCodes_WhenSearched_ThenNextCountsThePatientsWithTheirFullSystemUris()
    {
        using TestFolder folder = Folder();

        JsonNode result = await Search(Plain(folder), "diabet", resourceType: "Condition");

        JsonNode next = result["next"]!.AsArray().Single(call => (string)call!["tool"]! == "patient_cohort")!;
        next["args"]!["conditions"]![0]!["codes"]!.AsArray().Select(code => (string)code!).ShouldBe(
            [$"{Snomed}|44054006", $"{Snomed}|127013003", $"{Icd10Cm}|E11.9"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task GivenMoreMatchesThanAPage_WhenSearched_ThenThePageIsCutWithOmittedAndANextPage()
    {
        using TestFolder folder = Folder();

        JsonNode result = await Search(Plain(folder), "diabet", count: 2);

        ((int)result["facts"]!["total"]!, (int)result["facts"]!["returned"]!).ShouldBe((4, 2));
        result["omitted"]![0]!["what"]!.ToString().ShouldContain("2 more");
        JsonNode page = result["next"]!.AsArray().Single(call => (string)call!["tool"]! == "folder_code_systems")!;
        ((int)page["args"]!["offset"]!).ShouldBe(2);
        ((string)page["args"]!["text"]!).ShouldBe("diabet");
    }

    [Fact]
    public async Task GivenThousandsOfMatches_WhenSearched_ThenTheResponseStaysWithinTheBudgetAndSaysWhatWasCut()
    {
        string display = string.Concat(Enumerable.Repeat("diabetes ", 20));
        IEnumerable<string> many = Resources.Take(3).Concat(Enumerable.Range(0, 300).Select(index => Condition($"x{index}", "p1", Snomed, $"{1000 + index}", display + index, "active")));
        using TestFolder folder = Folder(many);

        var response = await new FolderTools(Plain(folder)).FolderCodeSystems(count: 50, text: "diabet");
        string text = ((ModelContextProtocol.Protocol.TextContentBlock)response.Content.Single()).Text;
        JsonNode result = JsonNode.Parse(text)!;

        Encoding.UTF8.GetByteCount(text).ShouldBeLessThanOrEqualTo(EnvelopeJson.DefaultBudget);
        ((int)result["facts"]!["returned"]!).ShouldBeLessThan(50);
        ((int)result["facts"]!["total"]!).ShouldBe(300);
        result["omitted"]![0]!["why"]!.ToString().ShouldBe("size-budget");
        ((int)result["next"]!.AsArray().Single(call => (string)call!["tool"]! == "folder_code_systems")!["args"]!["offset"]!)
            .ShouldBe((int)result["facts"]!["returned"]!);
    }

    [Fact]
    public async Task GivenNoTerminologyServerOrPackages_WhenSearched_ThenItAnswersFromTheFolderAlone()
    {
        using TestFolder folder = Folder();
        FolderSession session = Plain(folder);

        JsonNode result = await Search(session, "diabet");

        Codes(result).ShouldNotBeEmpty();
        session.Terminology.Current.ShouldBeNull();
        session.Terminology.Options.TxServer.ShouldBeNull();
        result["issues"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenOnlyNarrowingArguments_WhenCalled_ThenItAsksForTheText()
    {
        using TestFolder folder = Folder();

        JsonNode result = Json(await new FolderTools(Plain(folder)).FolderCodeSystems(resourceType: "Condition"));

        ((string)result["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
    }

    [Fact]
    public async Task GivenOnlySeparators_WhenSearched_ThenTheCallFailsAskingForWords()
    {
        using TestFolder folder = Folder();

        JsonNode result = await Search(Plain(folder), " | , ");

        ((string)result["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
    }
}
