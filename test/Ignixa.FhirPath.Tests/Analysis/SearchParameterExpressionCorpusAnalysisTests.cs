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
    public void GivenShippedCorpus_WhenAnalyzed_ThenErrorDiagnosticsMatchKnownUpstreamSpecDefects(
        FhirVersion version,
        int expectedParameterCount)
    {
        var corpus = SearchParameterExpressionCorpus.Load(version);
        var analyzer = new FhirPathAnalyzer(version.GetSchemaProvider());

        var failures = Analyze(corpus, analyzer)
            .Where(item => item.Result.Issues.Any(issue => issue.Severity == ValidationIssueSeverity.Error))
            .Select(item => $"{item.ParameterUrl}|{item.ResourceType}")
            .ToArray();

        corpus.Parameters.Count.ShouldBe(expectedParameterCount);
        failures.ShouldBe(KnownUpstreamSpecDefects[version], ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenShippedCorpus_WhenAnalyzed_ThenEveryConformantExpressionIsValidOrIndeterminate(
        FhirVersion version,
        int _)
    {
        var corpus = SearchParameterExpressionCorpus.Load(version);
        var analyzer = new FhirPathAnalyzer(version.GetSchemaProvider());

        var rejected = Analyze(corpus, analyzer)
            .Where(item => !KnownUpstreamSpecDefects[version].Contains($"{item.ParameterUrl}|{item.ResourceType}"))
            .Where(item => !item.Result.IsValidOrIndeterminate)
            .Select(item => $"{item.ParameterUrl}|{item.ResourceType}: {string.Join(" | ", item.Result.Errors)}")
            .ToArray();

        rejected.ShouldBeEmpty();
    }

    private static IEnumerable<(string ParameterUrl, string ResourceType, AnalysisResult Result)> Analyze(
        SearchParameterExpressionCorpus corpus,
        FhirPathAnalyzer analyzer)
    {
        return corpus.Parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Expression))
            .SelectMany(parameter => parameter.BaseResourceTypes.Select(resourceType =>
                (ParameterUrl: parameter.Url.ToString(),
                 ResourceType: resourceType,
                 Result: analyzer.Analyze(parameter.Expression!, resourceType))));
    }

    /// <summary>
    /// Parameter/base-resource pairs the analyzer reports as errors because the shipped expression is itself
    /// defective, not because analysis is imprecise.
    /// </summary>
    /// <remarks>
    /// The R5 and R6 <c>clinical-date</c> expression is missing the <c>Appointment.</c> prefix on one clause:
    /// <c>AllergyIntolerance.recordedDate | (start | requestedPeriod.start).first() | AuditEvent.recorded | ...</c>.
    /// For every base resource other than Appointment, <c>start</c> and <c>requestedPeriod</c> are decidably
    /// absent, so the clause can only ever be empty. This set asserts that the defect is still detected; it is
    /// not a suppression list.
    /// </remarks>
    private static IReadOnlyDictionary<FhirVersion, IReadOnlySet<string>> KnownUpstreamSpecDefects { get; } =
        new Dictionary<FhirVersion, IReadOnlySet<string>>
        {
            [FhirVersion.Stu3] = CreateFailures(),
            [FhirVersion.R4] = CreateFailures(),
            [FhirVersion.R4B] = CreateFailures(),
            [FhirVersion.R5] = CreateFailures(
                ("http://hl7.org/fhir/SearchParameter/clinical-date",
                [
                    "AdverseEvent", "AllergyIntolerance", "AuditEvent", "CarePlan", "CareTeam",
                    "ClinicalImpression", "Composition", "Consent", "DiagnosticReport", "DocumentReference",
                    "Encounter", "EpisodeOfCare", "FamilyMemberHistory", "Flag", "Immunization",
                    "ImmunizationEvaluation", "ImmunizationRecommendation", "Invoice", "List", "MeasureReport",
                    "NutritionIntake", "Observation", "Procedure", "ResearchSubject", "RiskAssessment",
                    "SupplyRequest"
                ])),
            [FhirVersion.R6] = CreateFailures(
                ("http://hl7.org/fhir/SearchParameter/clinical-date",
                [
                    "AllergyIntolerance", "AuditEvent", "CarePlan", "CareTeam", "ClinicalImpression",
                    "Composition", "Consent", "DiagnosticReport", "DocumentReference", "Encounter",
                    "EpisodeOfCare", "FamilyMemberHistory", "Flag", "Immunization", "ImmunizationEvaluation",
                    "ImmunizationRecommendation", "Invoice", "List", "MeasureReport", "NutritionIntake",
                    "Observation", "Procedure", "ResearchSubject", "RiskAssessment", "SupplyRequest"
                ]))
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
