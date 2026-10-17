using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Visitors;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// Expressions agents write against search results (cli-on-fhir columns and fhirpath, ADR 2610), which the analyzer must not
/// call invalid or always empty when the evaluator answers them. Each was mis-analysed before the fix it names, so each is a
/// regression test; together with the structured unresolved-property issue they let a caller block only on a decided miss.
/// </summary>
public class AgentExpressionAnalysisTests
{
    private readonly FhirPathAnalyzer _analyzer = new(FhirVersion.R4.GetSchemaProvider());

    // ResourceTypeNames compares case-insensitively, so medication and encounter were typed as the Medication and Encounter
    // resources: medication.ofType(Reference) was always empty and encounter.resolve() an error.
    [Theory]
    [InlineData("MedicationRequest", "medication.ofType(Reference).resolve().code.coding.first().display")]
    [InlineData("MedicationRequest", "(medication.ofType(CodeableConcept) | medication.ofType(Reference).resolve().code).coding.first().display")]
    [InlineData("Observation", "encounter.resolve().class.code")]
    [InlineData("Observation", "encounter.reference")]
    public void GivenAnElementNamedLikeAResourceType_WhenAnalyzed_ThenItIsNavigatedAsTheElement(string root, string expression)
    {
        AnalysisResult result = _analyzer.Analyze(expression, root);

        result.IsValidOrIndeterminate.ShouldBeTrue(string.Join("; ", result.Issues.Select(issue => issue.Message)));
        result.HasAlwaysEmptySubexpression.ShouldBeFalse();
    }

    [Fact]
    public void GivenAResourceTypeNameAtTheRoot_WhenAnalyzed_ThenItIsStillATypeNameNavigation()
    {
        AnalysisResult result = _analyzer.Analyze("Patient.name.family", "Patient");

        result.IsValid.ShouldBeTrue();
        result.TypeNames.ShouldBe(["string"]);
    }

    // entry.resource is typed as the abstract Resource, so every element after it was an error.
    [Theory]
    [InlineData("entry.resource.value.ofType(Quantity).value.max()")]
    [InlineData("entry.resource.where(value.ofType(Quantity).value > 7).count()")]
    [InlineData("entry.resource.subject.reference.distinct().count()")]
    public void GivenAnElementOfTheResourcesInABundle_WhenAnalyzed_ThenItIsIndeterminateNotAnError(string expression)
    {
        AnalysisResult result = _analyzer.Analyze(expression, "Bundle");

        result.IsValidOrIndeterminate.ShouldBeTrue(string.Join("; ", result.Issues.Select(issue => issue.Message)));
        result.IsIndeterminate.ShouldBeTrue();
        result.HasAlwaysEmptySubexpression.ShouldBeFalse();
    }

    [Fact]
    public void GivenANameNoResourceTypeDeclaresAfterAnAbstractFocus_WhenAnalyzed_ThenItIsAnUnresolvedPropertyError()
    {
        AnalysisResult result = _analyzer.Analyze("entry.resource.valeu", "Bundle");

        ValidationIssue issue = result.Issues.Single(issue => issue.IsUnresolvedProperty);
        issue.Severity.ShouldBe(ValidationIssueSeverity.Error);
        issue.Property.ShouldBe("valeu");
        issue.FocusTypes.ShouldBe(["Resource"]);
    }

    // ofType(Observation) on a Resource focus was always empty.
    [Fact]
    public void GivenAResourceTypeFilterOnAnAbstractFocus_WhenAnalyzed_ThenItNarrowsToThatType()
    {
        AnalysisResult result = _analyzer.Analyze("entry.resource.ofType(Observation).value.ofType(Quantity).value", "Bundle");

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Issues.Select(issue => issue.Message)));
        result.HasAlwaysEmptySubexpression.ShouldBeFalse();
        result.TypeNames.ShouldBe(["decimal"]);
    }

    // An unknown function passed its focus type through, so a host function such as patientSearch() on a Patient made every
    // element of the resources it returns an error on Patient.
    [Fact]
    public void GivenAnUnknownFunction_WhenItsResultIsNavigated_ThenNoPropertyIsCheckedAgainstTheFocusType()
    {
        AnalysisResult result = _analyzer.Analyze("patientSearch('Observation', 'code=4548-4').value.ofType(Quantity).value", "Patient");

        result.IsValidOrIndeterminate.ShouldBeTrue(string.Join("; ", result.Issues.Select(issue => issue.Message)));
        result.Issues.ShouldContain(issue => issue.Message == "Unknown function 'patientSearch'");
    }

    [Theory]
    [InlineData("Observation", "valueQuantiy", "valueQuantiy", "Observation")]
    [InlineData("Observation", "status.foo", "foo", "code")]
    [InlineData("Observation", "foo", "foo", "Observation")]
    public void GivenAPropertyNoFocusTypeDeclares_WhenAnalyzed_ThenTheIssueCarriesThePropertyAndFocusTypes(string root, string expression, string property, string focusType)
    {
        AnalysisResult result = _analyzer.Analyze(expression, root);

        ValidationIssue issue = result.Issues.First(issue => issue.IsUnresolvedProperty);
        issue.Severity.ShouldBe(ValidationIssueSeverity.Error);
        issue.Message.ShouldBe($"Property '{property}' not found on type '{focusType}'");
        issue.Property.ShouldBe(property);
        issue.FocusTypes.ShouldBe([focusType]);
    }
}
