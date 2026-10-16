// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The <c>patientSearch(type, query)</c> FHIRPath function: a search inside the focus Patient's compartment, so a cohort with
/// a condition, a latest lab and a missing-value rule is one Patient search. New capability: at 365d1acd the function did not
/// exist (an unknown FHIRPath function fails as invalid-parameter) and such a cohort took several calls.
/// </summary>
public class PatientSearchTests
{
    private const string Ihd = "code=http://snomed.info/sct|414545008";

    private const string Ldl = "code=http://loinc.org|18262-6";

    /// <summary>
    /// Chronic IHD (active) for <c>pa</c>, <c>pb</c>, <c>pc</c> and the deceased <c>pe</c>; resolved for <c>pd</c>; none for <c>pf</c>.
    /// LDL: <c>pa</c> 150; <c>pb</c> 120 then 50 (latest below 70); <c>pc</c> none.
    /// </summary>
    private static TestFolder Cohort() => new TestFolder()
        .Write("patients.ndjson", """
            {"resourceType":"Patient","id":"pa","birthDate":"1970-01-01"}
            {"resourceType":"Patient","id":"pb","birthDate":"1965-01-01"}
            {"resourceType":"Patient","id":"pc","birthDate":"1980-01-01"}
            {"resourceType":"Patient","id":"pd","birthDate":"1975-01-01"}
            {"resourceType":"Patient","id":"pe","birthDate":"1950-01-01","deceasedDateTime":"2025-12-01"}
            {"resourceType":"Patient","id":"pf","birthDate":"1990-01-01"}
            """)
        .Write("conditions.ndjson", string.Join('\n',
            Condition("ca", "pa", "active"),
            Condition("cb", "pb", "active"),
            Condition("cc", "pc", "active"),
            Condition("cd", "pd", "resolved"),
            Condition("ce", "pe", "active")))
        .Write("obs.ndjson", string.Join('\n',
            Lab("a1", "pa", "2024-01-01", 150),
            Lab("b1", "pb", "2024-01-01", 120),
            Lab("b2", "pb", "2025-06-01", 50)));

    private static string Condition(string id, string patient, string status) =>
        $$$"""{"resourceType":"Condition","id":"{{{id}}}","clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"{{{status}}}"}]},"code":{"coding":[{"system":"http://snomed.info/sct","code":"414545008"}]},"subject":{"reference":"Patient/{{{patient}}}"}}""";

    private static string Lab(string id, string patient, string date, int value) =>
        $$$"""{"resourceType":"Observation","id":"{{{id}}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"18262-6"}]},"subject":{"reference":"Patient/{{{patient}}}"},"effectiveDateTime":"{{{date}}}","valueQuantity":{"value":{{{value}}},"unit":"mg/dL"}}""";

    private static ToolEnvelope<SearchFacts?> Patients(FolderSession session, string fhirPath, string query = "deceased=false") =>
        ResourceSearcher.Search(session, new SearchRequest("Patient", query, FhirPath: fhirPath));

    private static IssueItem Error(ToolEnvelope<SearchFacts?> envelope)
    {
        envelope.Facts.ShouldBeNull();
        return envelope.Issues.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenACohortWithAConditionALatestLabAndAMissingValueRule_WhenAskedAsOnePatientSearch_ThenTheCountAndThePatientsCountedAreReturned()
    {
        using TestFolder folder = Cohort();
        const string expression = $"entry.resource.where(patientSearch('Condition','{Ihd}&clinical-status=active').exists() and (patientSearch('Observation','{Ldl}&_sort=-date&_count=1').value.ofType(Quantity).value >= 70 or patientSearch('Observation','{Ldl}').empty())).count()";

        ToolEnvelope<SearchFacts?> envelope = Patients(Open(folder), expression);

        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Value.ShouldNotBeNull().Select(item => item!.GetValue<int>()).ShouldBe([2]);
        facts.ValueFrom.ShouldBe(ExpressionSearch.FromCounted);
        facts.Counted.ShouldBe(["Patient/pa", "Patient/pc"]);
        envelope.Evidence.Select(item => item.Reference).ShouldBe(["Patient/pa", "Patient/pc"]);
        envelope.Issues.ShouldNotContain(issue => issue.Severity == "error");
    }

    [Fact]
    public void GivenSortAndCountInTheSubquery_WhenUsedInColumns_ThenEachRowSeesItsOwnPatientsMatchesInThatOrder()
    {
        using TestFolder folder = Cohort();
        var columns = SearchColumn.From(new Dictionary<string, string>
        {
            ["latest"] = $"patientSearch('Observation','{Ldl}&_sort=-date&_count=1').value.ofType(Quantity).value",
            ["n"] = $"patientSearch('Observation','{Ldl}').count()",
        });

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_id=pa,pb,pc", Columns: columns));

        envelope.Facts.ShouldNotBeNull().Rows.ShouldNotBeNull().Select(row => row.ToJsonString()).ShouldBe(
        [
            """["Patient/pa",150,1]""",
            """["Patient/pb",50,2]""",
            """["Patient/pc",null,0]""",
        ]);
    }

    [Fact]
    public void GivenConditionsOfAnyClinicalStatus_WhenTheSubqueryDoesNotNameOne_ThenNoneIsFilteredOut()
    {
        using TestFolder folder = Cohort();
        FolderSession session = Open(folder);

        ToolEnvelope<SearchFacts?> any = Patients(session, $"entry.resource.where(patientSearch('Condition','{Ihd}').exists()).count()");
        ToolEnvelope<SearchFacts?> active = Patients(session, $"entry.resource.where(patientSearch('Condition','{Ihd}&clinical-status=active').exists()).count()");

        any.Facts.ShouldNotBeNull().Counted.ShouldBe(["Patient/pa", "Patient/pb", "Patient/pc", "Patient/pd"]);
        active.Facts.ShouldNotBeNull().Counted.ShouldBe(["Patient/pa", "Patient/pb", "Patient/pc"]);
    }

    [Fact]
    public void GivenACountOfElementsRatherThanMatches_WhenEvaluated_ThenTheValueIsComputedFromTheMatchesAndNothingIsListedAsCounted()
    {
        using TestFolder folder = Cohort();

        ToolEnvelope<SearchFacts?> envelope = Patients(Open(folder), "entry.resource.birthDate.count()");

        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Value.ShouldNotBeNull().Select(item => item!.GetValue<int>()).ShouldBe([5]);
        facts.ValueFrom.ShouldBe(ExpressionSearch.FromMatches);
        facts.Counted.ShouldBeNull();
    }

    [Theory]
    [InlineData("Observation", "entry.resource.where(patientSearch('Condition').exists()).count()", "not a Observation")]
    [InlineData("Patient", "patientSearch('Condition').count()", "not a Bundle")]
    [InlineData("Patient", "entry.resource.where(patientSearch('Organization').exists()).count()", "never in a Patient compartment")]
    [InlineData("Patient", "entry.resource.where(patientSearch('Condition','_include=Condition:subject').exists()).count()", "_include")]
    [InlineData("Patient", "entry.resource.where(patientSearch(1).exists()).count()", "a single string")]
    public void GivenAMisusedPatientSearch_WhenEvaluated_ThenTheCallFailsSayingWhy(string resourceType, string expression, string message)
    {
        using TestFolder folder = Cohort();

        IssueItem issue = Error(ResourceSearcher.Search(Open(folder), new SearchRequest(resourceType, FhirPath: expression)));

        issue.Code.ShouldBe("invalid-parameter");
        issue.Diagnostics.ShouldContain(message);
    }

    [Fact]
    public void GivenASubqueryWithAnUnknownParameter_WhenEvaluated_ThenTheSearchErrorNamesTheSubquery()
    {
        using TestFolder folder = Cohort();

        IssueItem issue = Error(Patients(Open(folder), "entry.resource.where(patientSearch('Condition','bogus=1').exists()).count()"));

        issue.Code.ShouldBe("unknown-search-parameter");
        issue.Diagnostics.ShouldStartWith("patientSearch('Condition', 'bogus=1'): ");
    }

    [Fact]
    public void GivenTheSameSubqueryForMorePatientsThanTheQueryLimit_WhenEvaluated_ThenItIsSearchedOnceAndAnswers()
    {
        using TestFolder folder = Many(PatientSearch.MaxQueries + 1);

        ToolEnvelope<SearchFacts?> envelope = Patients(Open(folder), $"entry.resource.where(patientSearch('Observation','{Ldl}').exists()).count()", query: string.Empty);

        envelope.Facts.ShouldNotBeNull().Value.ShouldNotBeNull().Select(item => item!.GetValue<int>()).ShouldBe([PatientSearch.MaxQueries + 1]);
    }

    [Fact]
    public void GivenASubqueryThatDiffersPerPatient_WhenItPassesTheQueryLimit_ThenTheCallFailsAsTooManyMatches()
    {
        using TestFolder folder = Many(PatientSearch.MaxQueries + 1);

        IssueItem issue = Error(Patients(Open(folder), "entry.resource.where(patientSearch('Observation','subject=Patient/' + id).exists()).count()", query: string.Empty));

        issue.Code.ShouldBe("too-many-matches");
        issue.Diagnostics.ShouldContain($"{PatientSearch.MaxQueries} distinct subqueries");
    }

    [Fact]
    public void GivenSubqueriesThatHandOutMoreResourcesThanTheWorkLimit_WhenInvoked_ThenTheCallFailsAsTooManyMatches()
    {
        using TestFolder folder = Cohort();
        FolderSession session = Open(folder);
        var elements = new ResourceElements(session);
        var search = new PatientSearch(session, elements.Get, maxResources: 2);
        IElement pa = elements.Get(session.FindEntry("Patient", "pa")!)!;
        IElement pb = elements.Get(session.FindEntry("Patient", "pb")!)!;
        IReadOnlyList<IReadOnlyList<IElement>> arguments = [[Text(pa, "Observation")], [Text(pa, Ldl)]];

        search.Invoke([pb], arguments).Count().ShouldBe(2);
        search.Invoke([pb], arguments).Count().ShouldBe(2);
        ToolRequestException failure = Should.Throw<ToolRequestException>(() => search.Invoke([pa], arguments));

        failure.Issue.Code.ShouldBe("too-many-matches");
        failure.Issue.Diagnostics.ShouldContain("more than 2 resources");
    }

    /// <summary><paramref name="count"/> patients, each with one LDL.</summary>
    private static TestFolder Many(int count) => new TestFolder()
        .Write("patients.ndjson", string.Join('\n', Enumerable.Range(0, count).Select(index => $$$"""{"resourceType":"Patient","id":"p{{{index}}}"}""")))
        .Write("obs.ndjson", string.Join('\n', Enumerable.Range(0, count).Select(index => Lab($"o{index}", $"p{index}", "2025-01-01", 100))));

    /// <summary>A FHIRPath string, as the evaluator hands an argument over; <paramref name="input"/> is any element, since a literal ignores it.</summary>
    private static IElement Text(IElement input, string value) =>
        new FhirPathEvaluator().Evaluate(input, new FhirPathParser().Parse($"'{value}'")).Single();
}
