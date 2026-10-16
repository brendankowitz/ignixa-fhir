// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.ProjectionFixture;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>columns</c>: a search page as a table of FHIRPath values, one row per match keyed by its reference, with
/// <c>resolve()</c> reaching resources through urn:uuid and conditional references. New capability: at ab2447f3 a search
/// returned only whole (or top-level-trimmed) resources.
/// </summary>
public class ColumnSearchTests
{
    private const string Patient = "Patient/pa";

    private static SearchRequest Medications(IReadOnlyList<SearchColumn>? columns = null, string query = "_sort=authoredon") =>
        new("MedicationRequest", query, Compartment: Patient, Columns: columns ?? MedicationColumns);

    [Fact]
    public void GivenSyntheaRequests_WhenTheColumnsResolveTheConditionalRequester_ThenEachRowHasThePrescribersFamilyName()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Medications());

        SearchFacts facts = Facts(envelope);
        facts.Columns.ShouldBe(["ref", "date", "drug", "prescriber"]);
        Rows(envelope).ShouldBe([
            """["MedicationRequest/m1","2019-02-01","Amlodipine 5 MG Oral Tablet","Marquardt"]""",
            """["MedicationRequest/m2","2021-03-10","lisinopril 10 MG Oral Tablet","Okafor"]""",
            """["MedicationRequest/m3","2022-07-15","metformin 500 MG Oral Tablet",null]""",
            """["MedicationRequest/m4","2023-01-20","carvedilol 6.25 MG Oral Tablet","Marquardt"]""",
        ]);
        facts.Total.ShouldBe(4);
        facts.Returned.ShouldBe(4);
        facts.Resources.ShouldBeNull();
    }

    [Fact]
    public void GivenAResolvedColumn_WhenSearched_ThenEvidenceCitesEachRowsResourceAndEachResourceResolveReachedOnce()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Medications());

        Cited(envelope).ShouldBe(["MedicationRequest/m1", "Practitioner/pr1", "MedicationRequest/m2", "Practitioner/pr2", "MedicationRequest/m3", "MedicationRequest/m4", "Medication/med1"]);
        envelope.Evidence.Single(item => item.Reference == "Practitioner/pr1").File.ShouldBe(PractitionersFile);
    }

    [Fact]
    public void GivenAConditionalReferenceNoResourceMatches_WhenResolved_ThenTheCellIsEmptyCountedAndANoteNamesTheReference()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Medications());

        Facts(envelope).EmptyCells.ShouldBe(new Dictionary<string, int> { ["prescriber"] = 1 });
        IssueItem note = envelope.Issues.Single(issue => issue.Code == "resolve-target-absent");
        note.Severity.ShouldBe("information");
        note.Diagnostics.ShouldContain("Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|999");
        envelope.Issues.ShouldNotContain(issue => issue.Code == "column-empty");
    }

    [Fact]
    public void GivenAReportWhoseResultsAreUrnReferences_WhenAColumnResolvesThem_ThenTheCellHoldsTheResolvedObservationsValue()
    {
        using TestFolder folder = Synthea();
        IReadOnlyList<SearchColumn> columns =
        [
            new("date", "effective.ofType(dateTime)"),
            new("potassium", "result.resolve().where(code.coding.code = '6298-4').value.ofType(Quantity).value"),
            new("encounterClass", "encounter.resolve().class.code"),
        ];

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("DiagnosticReport", "code=http://loinc.org|51990-0", Compartment: Patient, Columns: columns));

        Rows(envelope).ShouldBe(["""["DiagnosticReport/dr1","2021-03-09T23:00:00Z",4.1,"EMER"]"""]);
        Cited(envelope).ShouldBe(["DiagnosticReport/dr1", "Observation/ona", "Observation/ok", "Encounter/e2"]);
    }

    [Fact]
    public void GivenAnEncounterWithAConditionalServiceProvider_WhenAColumnResolvesIt_ThenTheCellHoldsTheOrganizationsName()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", "class=EMER", Compartment: Patient, Columns: [new("provider", "serviceProvider.resolve().name")]));

        Rows(envelope).ShouldBe(["""["Encounter/e2","General Hospital"]"""]);
    }

    [Fact]
    public void GivenAPathYieldingSeveralValues_WhenUsedAsAColumn_ThenTheCellIsAJsonArrayOfThem()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Condition", Compartment: Patient, Columns: [new("codes", "code.coding.code"), new("first", "code.coding.first().code")]));

        Cell(envelope, 0, "codes")!.ToJsonString().ShouldBe("""["49436004","I48.91"]""");
        Cell(envelope, 0, "first")!.GetValue<string>().ShouldBe("49436004");
    }

    [Fact]
    public void GivenAPathYieldingAComplexElement_WhenUsedAsAColumn_ThenTheCellIsItsJson()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "code=http://loinc.org|6298-4", Compartment: Patient, Columns: [new("value", "valueQuantity")]));

        Cell(envelope, 0, "value")!.ToJsonString().ShouldBe("""{"value":4.1,"unit":"mmol/L"}""");
    }

    [Fact]
    public void GivenAPathMatchingNothingInAnyRow_WhenUsedAsAColumn_ThenItsCellsAreNullCountedAndAColumnEmptyWarningNamesThePath()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Medications([new("date", "authoredOn"), new("typo", "authoredon")]));

        Facts(envelope).EmptyCells.ShouldBe(new Dictionary<string, int> { ["typo"] = 4 });
        Facts(envelope).Rows.ShouldNotBeNull().ShouldAllBe(row => row[2] == null);
        IssueItem warning = envelope.Issues.Single(issue => issue.Code == "column-empty");
        warning.Severity.ShouldBe("warning");
        warning.Diagnostics.ShouldContain("'typo' (authoredon)");
    }

    [Fact]
    public void GivenEveryCellHasAValue_WhenSearched_ThenEmptyCellsIsLeftOut()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Medications([new("date", "authoredOn")]));

        Facts(envelope).EmptyCells.ShouldBeNull();
        envelope.Issues.ShouldNotContain(issue => issue.Code == "column-empty" || issue.Code == "resolve-target-absent");
    }

    [Theory]
    [InlineData("authoredOn.(", "is not valid FHIRPath")]
    [InlineData("notAFunction()", "could not be evaluated on MedicationRequest/m1")]
    [InlineData("authoredOn + status", "could not be evaluated on MedicationRequest/m1")]
    public void GivenAnInvalidExpression_WhenUsedAsAColumn_ThenTheCallFailsWithInvalidParameterQuotingIt(string path, string problem)
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Medications([new("date", "authoredOn"), new("bad", path)]));

        IssueItem error = Error(envelope);
        error.Code.ShouldBe("invalid-parameter");
        error.Diagnostics.ShouldContain($"Column 'bad' '{path}'");
        error.Diagnostics.ShouldContain(problem);
    }

    // A search matching nothing evaluates no row, so the expression is evaluated on an empty resource of the type instead.
    [Fact]
    public void GivenAnUnknownFunctionAndNoMatches_WhenUsedAsAColumn_ThenTheCallStillFails()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Medications([new("bad", "notAFunction()")], query: "status=draft"));

        Error(envelope).Diagnostics.ShouldContain("could not be evaluated on an empty MedicationRequest");
    }

    [Theory]
    [InlineData("summary", "summary=count")]
    [InlineData("elements", "'elements'")]
    [InlineData("_include", "_include")]
    [InlineData("_elements", "'elements'")]
    [InlineData("fhirpath", "not both")]
    public void GivenColumnsWithSomethingThatReturnsResources_WhenSearched_ThenTheCallFailsWithInvalidParameter(string other, string named)
    {
        using TestFolder folder = Synthea();
        SearchRequest request = other switch
        {
            "summary" => Medications() with { Summary = "count" },
            "elements" => Medications() with { Elements = "status" },
            "_include" => Medications(query: "_include=MedicationRequest:requester"),
            "_elements" => Medications(query: "_elements=status"),
            _ => Medications() with { FhirPath = "entry.count()" },
        };

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), request);

        IssueItem error = Error(envelope);
        error.Code.ShouldBe("invalid-parameter");
        error.Diagnostics.ShouldContain(named);
    }

    [Theory]
    [InlineData("ref", "key column")]
    [InlineData("", "blank name")]
    public void GivenAReservedOrBlankColumnName_WhenSearched_ThenTheCallFailsWithInvalidParameter(string name, string named)
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Medications([new(name, "status")]));

        Error(envelope).Diagnostics.ShouldContain(named);
    }

    [Fact]
    public void GivenNoColumns_WhenSearched_ThenTheCallFailsWithInvalidParameter()
    {
        using TestFolder folder = Synthea();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Medications([]));

        Error(envelope).Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public void GivenColumnsFromJson_WhenParsed_ThenTheyKeepTheirWrittenOrder()
    {
        IReadOnlyList<SearchColumn> columns = SearchColumn.FromJson("""{"z":"status","a":"authoredOn"}""");

        columns.ShouldBe([new SearchColumn("z", "status"), new SearchColumn("a", "authoredOn")]);
    }

    [Theory]
    [InlineData("""["status"]""")]
    [InlineData("""{"a":1}""")]
    [InlineData("{not json")]
    public void GivenColumnsJsonThatIsNotAnObjectOfStrings_WhenParsed_ThenItFailsWithInvalidParameter(string json)
    {
        ToolRequestException error = Should.Throw<ToolRequestException>(() => SearchColumn.FromJson(json));

        error.Issue.Code.ShouldBe("invalid-parameter");
    }

    /// <summary>Patient p1 with 120 observations whose note column is about 300 bytes, so a default page of 50 rows is cut at 12 KB.</summary>
    private static TestFolder ManyRows() => new TestFolder()
        .Write("patient.json", """{"resourceType":"Patient","id":"p1"}""")
        .Write("obs.ndjson", string.Join('\n', Enumerable.Range(0, 120).Select(i =>
            $$"""{"resourceType":"Observation","id":"o{{i:000}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"4548-4"}]},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"2020-01-{{i % 28 + 1:00}}","valueQuantity":{"value":{{i}},"unit":"%"},"note":[{"text":"{{new string('n', 300)}}"}]}""")));

    private static SearchRequest Notes(int offset = 0) =>
        new("Observation", Compartment: "Patient/p1", Offset: offset, Columns: [new("value", "valueQuantity.value"), new("note", "note.text")]);

    [Fact]
    public void GivenMoreRowsThanTheBudgetHolds_WhenSearchedWithColumns_ThenTheTableIsCutWithinTwelveKilobytesAndOmittedCountsTheRest()
    {
        using TestFolder folder = ManyRows();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), Notes());

        SearchFacts facts = Facts(envelope);
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.DefaultBudget);
        facts.Returned.ShouldBeInRange(10, ColumnSearch.DefaultRows - 1);
        facts.Rows.ShouldNotBeNull().Count.ShouldBe(facts.Returned);
        envelope.Evidence.Count.ShouldBe(facts.Returned);
        envelope.Omitted.ShouldHaveSingleItem().ShouldBe(new OmittedItem($"{120 - facts.Returned} more matches (offset={facts.Returned})", "size-budget"));
    }

    [Fact]
    public void GivenACutTable_WhenTheNextCallIsFollowed_ThenItContinuesWithTheSameColumnsAndOffersNoElementsTrim()
    {
        using TestFolder folder = ManyRows();
        FolderSession session = Open(folder);
        ToolEnvelope<SearchFacts?> first = ResourceSearcher.Search(session, Notes());
        int returned = Facts(first).Returned;

        NextCall rest = first.Next[0];
        ToolEnvelope<SearchFacts?> second = ResourceSearcher.Search(session, Notes(offset: (int)rest.Args["offset"]));

        rest.Args["offset"].ShouldBe(returned);
        ((Dictionary<string, string>)rest.Args["columns"]).ShouldBe(new Dictionary<string, string> { ["value"] = "valueQuantity.value", ["note"] = "note.text" });
        first.Next.ShouldNotContain(call => call.Args.ContainsKey("elements"));
        Rows(second)[0].ShouldStartWith($"""["Observation/o{returned:000}",{returned},""");
    }

    [Fact]
    public void GivenSmallRows_WhenSearchedWithColumns_ThenTheDefaultPageHoldsFiftyRowsWithinTheBudget()
    {
        using TestFolder folder = ManyRows();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Compartment: "Patient/p1", Columns: [new("value", "valueQuantity.value")]));

        Facts(envelope).Returned.ShouldBe(ColumnSearch.DefaultRows);
        envelope.Omitted.ShouldHaveSingleItem().ShouldBe(new OmittedItem("70 more matches (offset=50)", "limit"));
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.DefaultBudget);
    }

    [Fact]
    public void GivenARowTooLargeForTheBudget_WhenSearchedWithColumns_ThenItIsNamedInOmittedAndPagingMovesPastIt()
    {
        using TestFolder folder = new TestFolder()
            .Write("obs.ndjson", $$$"""
                {"resourceType":"Observation","id":"big","status":"final","code":{"text":"x"},"note":[{"text":"{{{new string('n', 3000)}}}"}]}
                {"resourceType":"Observation","id":"small","status":"final","code":{"text":"x"}}
                """);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", MaxBytes: EnvelopeJson.MinBudget, Columns: [new("note", "note.text")]));

        Rows(envelope).ShouldBe(["""["Observation/small",null]"""]);
        envelope.Omitted.ShouldContain(item => item.What.StartsWith("the row of Observation/big is ", StringComparison.Ordinal) && item.Why == "size-budget");
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.MinBudget);
    }

    [Fact]
    public void GivenACutPageOfResources_WhenSearched_ThenNextOffersTheSamePageAsATableThatFitsWhole()
    {
        using TestFolder folder = ManyRows();
        FolderSession session = Open(folder);
        ToolEnvelope<SearchFacts?> resources = ResourceSearcher.Search(session, new SearchRequest("Observation", Compartment: "Patient/p1", Count: 20));

        NextCall table = resources.Next.Single(call => call.Args.ContainsKey("columns"));
        var columns = (Dictionary<string, string>)table.Args["columns"];
        ToolEnvelope<SearchFacts?> rows = ResourceSearcher.Search(session, new SearchRequest("Observation", Compartment: "Patient/p1", Count: 20, Columns: SearchColumn.From(columns)));

        table.Hint.ShouldNotBeNullOrEmpty();
        table.Args.ShouldNotContainKey("offset");
        Facts(rows).Returned.ShouldBe(20);
        Cell(rows, 0, "value")!.GetValue<decimal>().ShouldBe(0m);
        rows.Issues.ShouldNotContain(issue => issue.Severity == "error");
    }

    [Fact]
    public void GivenCountAboveTheRowMaximum_WhenSearchedWithColumns_ThenItIsClampedWithAWarning()
    {
        using TestFolder folder = ManyRows();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Compartment: "Patient/p1", Count: 500, Columns: [new("value", "valueQuantity.value")]));

        envelope.Issues.ShouldContain(issue => issue.Code == "count-clamped" && issue.Diagnostics.Contains($"maximum of {ColumnSearch.MaxRows}", StringComparison.Ordinal));
        Facts(envelope).Returned.ShouldBe(120);
    }

    [Fact]
    public void GivenATable_WhenSerialized_ThenItIsAFractionOfTheResourcesItWasReadFrom()
    {
        using TestFolder folder = ManyRows();
        FolderSession session = Open(folder);
        var request = new SearchRequest("Observation", Compartment: "Patient/p1", Count: 10, MaxBytes: EnvelopeJson.MaxBytes);

        int resources = EnvelopeJson.SizeOf(ResourceSearcher.Search(session, request));
        int table = EnvelopeJson.SizeOf(ResourceSearcher.Search(session, request with { Columns = [new("date", "effectiveDateTime"), new("value", "valueQuantity.value")] }));

        table.ShouldBeLessThan(resources / 2);
    }
}
