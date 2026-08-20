using Ignixa.Abstractions;
using Ignixa.Benchmarks.Firely5;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Visitors;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

public class SearchParameterExpressionCorpusAnalysisTests
{
    public static TheoryData<FhirVersion, int> Versions => new()
    {
        { FhirVersion.Stu3, 1246 },
        { FhirVersion.R4, 1403 },
        { FhirVersion.R4B, 1437 },
        { FhirVersion.R5, 1242 },
        { FhirVersion.R6, 1288 }
    };

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenShippedCorpus_WhenAnalyzed_ThenNoFailuresAppearBeyondPinnedBaseline(
        FhirVersion version,
        int expectedParameterCount)
    {
        var corpus = SearchParameterExpressionCorpus.Load(version);
        var analyzer = new FhirPathAnalyzer(version.GetSchemaProvider());

        var failures = corpus.Parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Expression))
            .SelectMany(parameter => parameter.BaseResourceTypes.Select(resourceType => new
            {
                ParameterUrl = parameter.Url,
                ResourceType = resourceType,
                Result = analyzer.Analyze(parameter.Expression!, resourceType)
            }))
            .Where(item => item.Result.Issues.Any(issue => issue.Severity == ValidationIssueSeverity.Error))
            .Select(item => $"{item.ParameterUrl}|{item.ResourceType}")
            .ToArray();

        corpus.Parameters.Count.ShouldBe(expectedParameterCount);
        failures.Except(PinnedBaselineFailures[version], StringComparer.Ordinal).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenShippedCorpus_WhenAnalyzed_ThenNoExpressionsHaveErrorDiagnostics(
        FhirVersion version,
        int _)
    {
        var corpus = SearchParameterExpressionCorpus.Load(version);
        var analyzer = new FhirPathAnalyzer(version.GetSchemaProvider());

        var failures = corpus.Parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Expression))
            .SelectMany(parameter => parameter.BaseResourceTypes.Select(resourceType => new
            {
                Parameter = parameter,
                ResourceType = resourceType,
                Result = analyzer.Analyze(parameter.Expression!, resourceType)
            }))
            .Where(item => item.Result.Issues.Any(issue => issue.Severity == ValidationIssueSeverity.Error))
            .Select(item =>
                $"{item.Parameter.Url}|{item.ResourceType}: {string.Join(" | ", item.Result.Errors)}")
            .ToArray();

        failures.ShouldBeEmpty();
    }

    private static IReadOnlyDictionary<FhirVersion, IReadOnlySet<string>> PinnedBaselineFailures { get; } =
        new Dictionary<FhirVersion, IReadOnlySet<string>>
        {
            [FhirVersion.Stu3] = CreateFailures(
                ("http://hl7.org/fhir/SearchParameter/ConceptMap-product", ["ConceptMap"])),
            [FhirVersion.R4] = CreateFailures(
                ("http://hl7.org/fhir/SearchParameter/ConceptMap-product", ["ConceptMap"])),
            [FhirVersion.R4B] = CreateFailures(
                ("http://hl7.org/fhir/SearchParameter/ConceptMap-product", ["ConceptMap"])),
            [FhirVersion.R5] = CreateFailures(
                ("http://hl7.org/fhir/SearchParameter/BodyStructure-excludedstructure", ["BodyStructure"]),
                ("http://hl7.org/fhir/SearchParameter/clinical-date",
                [
                    "AdverseEvent", "AllergyIntolerance", "AuditEvent", "CarePlan", "CareTeam",
                    "ClinicalImpression", "Composition", "Consent", "DiagnosticReport", "DocumentReference",
                    "Encounter", "EpisodeOfCare", "FamilyMemberHistory", "Flag", "Immunization",
                    "ImmunizationEvaluation", "ImmunizationRecommendation", "Invoice", "List", "MeasureReport",
                    "NutritionIntake", "Observation", "Procedure", "ResearchSubject", "RiskAssessment",
                    "SupplyRequest"
                ]),
                ("http://hl7.org/fhir/SearchParameter/Composition-section-text", ["Composition"])),
            [FhirVersion.R6] = CreateFailures(
                ("http://hl7.org/fhir/SearchParameter/BodyStructure-excludedstructure", ["BodyStructure"]),
                ("http://hl7.org/fhir/SearchParameter/clinical-date",
                [
                    "AllergyIntolerance", "AuditEvent", "CarePlan", "CareTeam", "ClinicalImpression",
                    "Composition", "Consent", "DiagnosticReport", "DocumentReference", "Encounter",
                    "EpisodeOfCare", "FamilyMemberHistory", "Flag", "Immunization", "ImmunizationEvaluation",
                    "ImmunizationRecommendation", "Invoice", "List", "MeasureReport", "NutritionIntake",
                    "Observation", "Procedure", "ResearchSubject", "RiskAssessment", "SupplyRequest"
                ]),
                ("http://hl7.org/fhir/SearchParameter/Composition-section-text", ["Composition"]),
                ("http://hl7.org/fhir/SearchParameter/Specimen-container-location", ["Specimen"]),
                ("http://hl7.org/fhir/SearchParameter/Specimen-organization", ["Specimen"]))
        };

    private static IReadOnlySet<string> CreateFailures(
        params (string ParameterUrl, string[] ResourceTypes)[] parameters)
    {
        return parameters
            .SelectMany(parameter => parameter.ResourceTypes.Select(
                resourceType => $"{parameter.ParameterUrl}|{resourceType}"))
            .ToHashSet(StringComparer.Ordinal);
    }
}
