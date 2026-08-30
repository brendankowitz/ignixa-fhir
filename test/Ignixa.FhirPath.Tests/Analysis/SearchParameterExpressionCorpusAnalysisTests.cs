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

    public static TheoryData<FhirVersion, int, int> AnalysisFigures => new()
    {
        { FhirVersion.Stu3, 0, 0 },
        { FhirVersion.R4, 0, 59 },
        { FhirVersion.R4B, 0, 72 },
        { FhirVersion.R5, 26, 109 },
        { FhirVersion.R6, 25, 110 }
    };

    public static TheoryData<FhirVersion, int> LegacyCapitalizedCastVersions => new()
    {
        { FhirVersion.Stu3, 11 },
        { FhirVersion.R4, 1 },
        { FhirVersion.R4B, 1 }
    };

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenShippedCorpus_WhenAnalyzed_ThenAlwaysEmptyDiagnosticsMatchKnownUpstreamSpecDefects(
        FhirVersion version,
        int expectedParameterCount)
    {
        var corpus = SearchParameterExpressionCorpus.Load(version);
        var analyzer = new FhirPathAnalyzer(version.GetSchemaProvider());

        var analyzed = Analyze(corpus, analyzer).ToArray();

        var reported = analyzed
            .Where(item => item.Result.HasAlwaysEmptySubexpression)
            .Select(item => $"{item.ParameterUrl}|{item.ResourceType}")
            .ToArray();

        var causes = analyzed
            .SelectMany(item => item.Result.Issues)
            .Where(issue => issue.IsAlwaysEmpty)
            .Select(issue => issue.Message)
            .ToArray();

        corpus.Parameters.Count.ShouldBe(expectedParameterCount);
        reported.ShouldBe(KnownUpstreamSpecDefects[version], ignoreOrder: true);
        causes.ShouldAllBe(message =>
            message.Contains("'start'", StringComparison.Ordinal) ||
            message.Contains("'requestedPeriod'", StringComparison.Ordinal));
    }

    /// <summary>
    /// Guards the false-positive claim without pinning the cascade that currently produces the errors.
    /// </summary>
    /// <remarks>
    /// The reclassified diagnostic is a warning; the <c>Error</c> on the defective pairs arrives
    /// downstream, from the guard in <c>VisitChild</c> that rejects <c>.start</c> navigating off an
    /// emptied type set. That guard is over-strict — strict FHIRPath defines navigation off empty as
    /// empty — so fixing it would legitimately drop those errors to zero while the defect stayed fully
    /// reported by the always-empty assertion above. This test therefore only asserts that no error
    /// appears outside the known-defective pairs, so a later guard fix does not read as lost detection.
    /// </remarks>
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

    [Theory]
    [MemberData(nameof(AnalysisFigures))]
    public void GivenShippedCorpus_WhenAnalyzed_ThenErrorAndInvalidFiguresDoNotRegress(
        FhirVersion version,
        int expectedErrorResults,
        int expectedInvalidResults)
    {
        // Arrange
        var corpus = SearchParameterExpressionCorpus.Load(version);
        var analyzer = new FhirPathAnalyzer(version.GetSchemaProvider());

        // Act
        var analyzed = Analyze(corpus, analyzer).Select(item => item.Result).ToArray();

        // Assert
        analyzed.Count(result => result.Errors.Any()).ShouldBe(expectedErrorResults);
        analyzed.Count(result => !result.IsValid).ShouldBe(expectedInvalidResults);
    }

    [Theory]
    [MemberData(nameof(LegacyCapitalizedCastVersions))]
    public void GivenShippedPreR5CapitalizedCasts_WhenAnalyzed_ThenEveryAliasRemainsValid(
        FhirVersion version,
        int expectedCastCount)
    {
        // Arrange
        var corpus = SearchParameterExpressionCorpus.Load(version);
        var analyzer = new FhirPathAnalyzer(version.GetSchemaProvider());

        // Act
        var analyzed = corpus.Parameters
            .SelectMany(parameter => EnumerateExpressions(parameter)
                .Where(ContainsLegacyCapitalizedCast)
                .SelectMany(expression => parameter.BaseResourceTypes.Select(rootType =>
                    (parameter.Url, Expression: expression, RootType: rootType, Result: analyzer.Analyze(expression, rootType)))))
            .ToArray();

        // Assert
        analyzed.Select(item => (item.Url, item.Expression)).Distinct().Count().ShouldBe(expectedCastCount);
        analyzed.ShouldAllBe(item => item.Result.IsValid);
        analyzed.ShouldAllBe(item => !item.Result.HasAlwaysEmptySubexpression);
        analyzed.ShouldAllBe(item => item.Result.InferredTypes.Types.Count > 0);
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

    private static IEnumerable<string> EnumerateExpressions(
        Ignixa.Search.Models.SearchParameterInfo parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter.Expression))
        {
            yield return parameter.Expression;
        }

        foreach (var component in parameter.Component)
        {
            if (!string.IsNullOrWhiteSpace(component.Expression))
            {
                yield return component.Expression;
            }
        }
    }

    private static bool ContainsLegacyCapitalizedCast(string expression) =>
        expression.Contains(".as(DateTime)", StringComparison.Ordinal)
        || expression.Contains(".as(Date)", StringComparison.Ordinal)
        || expression.Contains(".as(String)", StringComparison.Ordinal)
        || expression.Contains(".as(Uri)", StringComparison.Ordinal);

    /// <summary>
    /// Parameter/base-resource pairs the analyzer reports on because the shipped expression is itself
    /// defective, not because analysis is imprecise.
    /// </summary>
    /// <remarks>
    /// One defective parameter, counted once per base resource it applies to. The R5 and R6
    /// <c>clinical-date</c> expression is missing the <c>Appointment.</c> prefix on one clause:
    /// <c>AllergyIntolerance.recordedDate | (start | requestedPeriod.start).first() | AuditEvent.recorded | ...</c>.
    /// For every base resource other than Appointment, <c>start</c> and <c>requestedPeriod</c> are
    /// decidably absent, so the clause can only ever be empty. The set is anchored on the always-empty
    /// warnings naming those two elements, which is what the analyzer actually concludes; the accompanying
    /// <c>Error</c> is a downstream cascade and is deliberately not pinned. This is a detection assertion,
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
