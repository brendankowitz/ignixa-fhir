// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.Specification.Generated;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// FhirPathAnalyzer hints on <c>columns</c> and <c>fhirpath</c> (ADR 2610). New capability: before V14 an element a type does
/// not declare evaluated to nothing, a null column and a column-empty warning, and the agent had to guess why.
/// </summary>
public class FhirPathHintsTests
{
    private static readonly R4CoreSchemaProvider Schema = new();

    private static TestFolder Labs() => new TestFolder().Write("labs.ndjson", """
        {"resourceType":"Patient","id":"p1","birthDate":"1980-01-01"}
        {"resourceType":"Observation","id":"o1","status":"final","subject":{"reference":"Patient/p1"},"code":{"coding":[{"system":"http://loinc.org","code":"4548-4"}]},"effectiveDateTime":"2020-01-01","valueQuantity":{"value":5.8,"unit":"%"}}
        {"resourceType":"Observation","id":"o2","status":"final","subject":{"reference":"Patient/p1"},"code":{"coding":[{"system":"http://loinc.org","code":"4548-4"}]},"effectiveDateTime":"2021-01-01","valueQuantity":{"value":6.1,"unit":"%"}}
        """);

    [Theory]
    [InlineData("statuss", "'statuss' is not an element of Observation, so it never yields a value; did you mean 'status'?")]
    [InlineData("subjectt.reference", "did you mean 'subject'?")]
    [InlineData("code.codingg.code", "'codingg' is not an element of CodeableConcept")]
    public void GivenAColumnNamingAnUnknownElement_WhenSearched_ThenTheCallFailsBeforeEvaluationWithADidYouMean(string path, string hint)
    {
        using TestFolder folder = Labs();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Columns: [new("v", path)]));

        envelope.Facts.ShouldBeNull();
        IssueItem error = envelope.Issues.ShouldHaveSingleItem();
        error.Code.ShouldBe("invalid-parameter");
        error.Diagnostics.ShouldStartWith("Column 'v' ");
        error.Diagnostics.ShouldContain(hint);
    }

    [Theory]
    [InlineData("valueQuantiy.value", "'valueQuantiy' is not an element of Observation: value is one of Quantity, CodeableConcept, string, boolean, integer, Range, Ratio, SampledData, time, dateTime, Period. Write value.ofType(Quantity) (or valueQuantity).")]
    [InlineData("effectiveDate", "effective is one of dateTime, Period, Timing, instant. Write effective.ofType(dateTime) (or effectiveDateTime).")]
    public void GivenAChoiceElementSpeltWithATypeItDoesNotHave_WhenSearched_ThenTheHintNamesItsTypesAndTheOfTypeSpelling(string path, string hint)
    {
        using TestFolder folder = Labs();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Columns: [new("v", path)]));

        envelope.Issues.ShouldHaveSingleItem().Diagnostics.ShouldContain(hint);
    }

    [Theory]
    [InlineData("valueQuantity.value")]
    [InlineData("value.ofType(Quantity).value")]
    [InlineData("effectiveDateTime")]
    public void GivenAChoiceElementSpeltWithOneOfItsTypes_WhenSearched_ThenTheValuesComeBackWithNoHint(string path)
    {
        using TestFolder folder = Labs();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Columns: [new("v", path)]));

        envelope.Facts.ShouldNotBeNull().Rows.ShouldNotBeNull().Count.ShouldBe(2);
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAColumnOfAnElementOnlyOtherResourceTypesHave_WhenSearched_ThenTheRowsComeBackWithANeverYieldsWarning()
    {
        using TestFolder folder = Labs();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", Columns: [new("s", "status"), new("b", "birthDate")]));

        envelope.Facts.ShouldNotBeNull().Rows.ShouldNotBeNull().ShouldHaveSingleItem();
        IssueItem warning = envelope.Issues.First(issue => issue.Code == FhirPathHints.NeverYieldsCode);
        warning.Severity.ShouldBe("warning");
        warning.Diagnostics.ShouldBe("Column 's' can never yield a value here: Property 'status' will always be empty on root type 'Patient'. It is declared by another resource type, but not by this one.");
    }

    [Fact]
    public void GivenAFhirpathNamingAnElementNoResourceHas_WhenSearched_ThenTheHintDescribesTheSearchedType()
    {
        using TestFolder folder = Labs();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", FhirPath: "entry.resource.valueQuantiy.value.max()"));

        envelope.Facts.ShouldBeNull();
        IssueItem error = envelope.Issues.ShouldHaveSingleItem();
        error.Diagnostics.ShouldStartWith("'fhirpath' 'valueQuantiy' is not an element of Observation: value is one of");
    }

    [Theory]
    [InlineData("entry.resource.value.ofType(Quantity).value.max()", "[6.1]")]
    [InlineData("entry.resource.valueQuantity.value.max()", "[6.1]")]
    [InlineData("entry.resource.where(value.ofType(Quantity).value > 6).count()", "[1]")]
    [InlineData("entry.resource.ofType(Observation).value.ofType(Quantity).value.min()", "[5.8]")]
    public void GivenAFhirpathTheEvaluatorAnswers_WhenSearched_ThenItIsAnsweredWithNoHint(string expression, string value)
    {
        using TestFolder folder = Labs();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", FhirPath: expression));

        envelope.Facts.ShouldNotBeNull().Value!.ToJsonString().ShouldBe(value);
        envelope.Issues.ShouldBeEmpty();
    }

    // The expressions the existing column, fhirpath, cohort and help examples use, each answered by the evaluator: the hints
    // must neither refuse nor warn on any of them.
    [Theory]
    [InlineData("MedicationRequest", "authoredOn")]
    [InlineData("MedicationRequest", "status")]
    [InlineData("MedicationRequest", "requester.resolve().name.first().family")]
    [InlineData("MedicationRequest", "medication.ofType(Reference).resolve().code.coding.first().display")]
    [InlineData("MedicationRequest", "(medication.ofType(CodeableConcept) | medication.ofType(Reference).resolve().code).coding.first().display")]
    [InlineData("MedicationRequest", "dosageInstruction.timing.repeat.frequency")]
    [InlineData("Observation", "valueQuantity")]
    [InlineData("Observation", "valueQuantity.value")]
    [InlineData("Observation", "effective.ofType(dateTime)")]
    [InlineData("Observation", "effectiveDateTime")]
    [InlineData("Observation", "note.text")]
    [InlineData("Observation", "code.coding.code")]
    [InlineData("Observation", "code.coding.where(system='http://loinc.org').code")]
    [InlineData("Observation", "encounter.resolve().class.code")]
    [InlineData("Observation", "subject.resolve().birthDate")]
    [InlineData("Observation", "subject.reference")]
    [InlineData("Observation", "component.where(code.coding.code='8480-6').value.ofType(Quantity).value")]
    [InlineData("Observation", "extension('http://example.org/x').value")]
    [InlineData("Observation", "issued.toString().substring(0, 10)")]
    [InlineData("Observation", "iif(value.exists(), 'y', 'n')")]
    [InlineData("Condition", "code.coding.first().code")]
    [InlineData("Condition", "onset.ofType(dateTime)")]
    [InlineData("Condition", "onsetDateTime")]
    [InlineData("Condition", "abatement.exists()")]
    [InlineData("DiagnosticReport", "result.resolve().where(code.coding.code = '6298-4').value.ofType(Quantity).value")]
    [InlineData("Encounter", "serviceProvider.resolve().name")]
    [InlineData("Encounter", "participant.individual.resolve().name.family")]
    [InlineData("Encounter", "period.start")]
    [InlineData("Immunization", "occurrence.ofType(dateTime)")]
    [InlineData("Immunization", "vaccineCode.coding.display")]
    [InlineData("Patient", "name.where(use='official').given.first()")]
    [InlineData("Patient", "identifier.where(type.coding.code='MR').value")]
    [InlineData("Patient", "deceasedDateTime")]
    [InlineData("Patient", "deceased.exists().not() or deceased.ofType(boolean) = false")]
    [InlineData("Patient", "patientSearch('Observation','code=4548-4&_sort=-date&_count=1').value.ofType(Quantity).value")]
    public void GivenAColumnTheEvaluatorAnswers_WhenHinted_ThenNothingIsRefusedOrWarned(string resourceType, string path)
    {
        IReadOnlyList<ColumnSearch.CompiledColumn> columns = ColumnSearch.Compile([new SearchColumn("c", path)]);

        FhirPathHints.ForColumns(Schema, resourceType, columns).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Observation", "entry.count()")]
    [InlineData("Observation", "entry.resource.first().value.ofType(Quantity)")]
    [InlineData("Observation", "entry.resource.subject.reference.distinct().count()")]
    [InlineData("Observation", "entry.resource.where(value.ofType(Quantity).value >= 130 and subject.resolve().deceased.exists().not() and subject.resolve().birthDate <= @2008-08-17).count()")]
    [InlineData("MedicationRequest", "entry.resource.where((medication.ofType(CodeableConcept) | medication.ofType(Reference).resolve().code).coding.where(code in ('314231' | '617314')).exists()).count()")]
    [InlineData("Patient", "entry.resource.where(patientSearch('Condition','code=http://snomed.info/sct|414545008&clinical-status=active').exists()).count()")]
    public void GivenAFhirpathFromTheExamples_WhenHinted_ThenNothingIsRefusedOrWarned(string resourceType, string expression)
    {
        FhirPathHints.ForFhirPath(Schema, resourceType, ExpressionSearch.Compile(expression)).ShouldBeEmpty();
    }
}
