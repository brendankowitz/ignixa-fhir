using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal static class ResourceBackedKnownDivergences
{
    public const string ChoiceCast = "Typed choice casts over the shared adapter";
    public const string TemporalCarrier = "Instant versus dateTime carrier";
    public const string QuantityCollections = "Firely rejects resource-backed quantity collections";
    public const string QuantityEquivalence = "Quantity approximate equivalence is asymmetric";
    public const string TemporalOrdering = "Firely rejects resource-backed temporal ordering";

    public static IReadOnlyDictionary<string, int> ExpectedSelectCounts { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [ChoiceCast] = 8,
            [TemporalCarrier] = 2,
            [QuantityCollections] = 100,
            [QuantityEquivalence] = 5,
            [TemporalOrdering] = 5,
        };

    public static IReadOnlyDictionary<string, int> ExpectedIndexResourceCounts { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [ChoiceCast] = 9,
            [TemporalCarrier] = 2,
        };

    /// <summary>
    /// Evaluations where both engines threw. Pinned at zero because a mutual throw satisfies
    /// <c>ParityOutcome.Matches</c> without either engine producing a comparable value, so it is
    /// agreement the harness asserts but never established. Any non-zero value here is a finding.
    /// </summary>
    public const int ExpectedBothThrew = 0;

    /// <summary>
    /// Evaluations where both engines returned no results.
    /// </summary>
    /// <remarks>
    /// This is legitimate agreement - a search parameter expression that matches nothing on a resource
    /// is the common case - but it is agreement on absence, so it is much weaker evidence than a
    /// matched value and it is invisible to every divergence-based assertion. Pinning it exactly means
    /// the composition of the sweep cannot drift silently: 9,453 of 19,647 evaluations agree on empty,
    /// leaving 10,074 that compare real values. If an engine change moves this number the pin has to be
    /// updated deliberately, with the shift understood, rather than absorbed into an unchanged
    /// divergence count.
    /// </remarks>
    public const int ExpectedBothEmpty = 9453;

    /// <summary>
    /// Lower bounds on sweep size, so a corpus that stopped generating resources or expressions fails
    /// instead of trivially satisfying the divergence counts with nothing to compare.
    /// </summary>
    public const int MinimumSelectEvaluationsPerEngine = 19647;

    public const int MinimumResourceCount = 788;

    public static ResourceParityClassification? Classify(ParityDivergence divergence)
    {
        if (divergence.Source == "SearchParameter")
        {
            if (divergence.ResourceName.StartsWith("Stu3/", StringComparison.Ordinal)
                && divergence.Expression.Contains(".as(", StringComparison.Ordinal)
                && ReturnedEmptyVersusValue(divergence))
            {
                return Blocking(ChoiceCast);
            }

            if ((divergence.ResourceName.StartsWith("R5/", StringComparison.Ordinal)
                    || divergence.ResourceName.StartsWith("R6/", StringComparison.Ordinal))
                && divergence.Firely.Results.Count == 1
                && divergence.Firely.Results[0].StartsWith(
                    "DATETIME|temporal:System.DateTime|",
                    StringComparison.Ordinal)
                && divergence.Ignixa.Results.Count == 1
                && divergence.Ignixa.Results[0].StartsWith(
                    "INSTANT|temporal:instant|",
                    StringComparison.Ordinal))
            {
                return Blocking(TemporalCarrier);
            }

            return null;
        }

        if (divergence.ResourceName.EndsWith("/temporal-precision-offset", StringComparison.Ordinal)
            && divergence.Expression == "component.value.sort()"
            && FirelyRejected(divergence))
        {
            return NonBlocking(TemporalOrdering);
        }

        if (divergence.Expression == "component.value.first() ~ component.value.skip(1).first()"
            && divergence.Firely.Results.SequenceEqual(["BOOLEAN|boolean|false"], StringComparer.Ordinal)
            && divergence.Ignixa.Results.SequenceEqual(["BOOLEAN|boolean|true"], StringComparer.Ordinal))
        {
            return NonBlocking(QuantityEquivalence);
        }

        if ((divergence.ResourceName.Contains("/cardinality-", StringComparison.Ordinal)
                || divergence.ResourceName.EndsWith("/quantity-units", StringComparison.Ordinal))
            && IsQuantityCollectionExpression(divergence.Expression)
            && FirelyRejected(divergence))
        {
            return NonBlocking(QuantityCollections);
        }

        return null;
    }

    public static ResourceParityClassification? Classify(SearchIndexDivergence divergence)
    {
        var differingUrls = divergence.FirelyEntries
            .Except(divergence.IgnixaEntries, StringComparer.Ordinal)
            .Concat(divergence.IgnixaEntries.Except(divergence.FirelyEntries, StringComparer.Ordinal))
            .Select(IndexUrl)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (divergence.Version is FhirVersion.R5 or FhirVersion.R6
            && differingUrls.SequenceEqual(
                ["http://hl7.org/fhir/SearchParameter/clinical-date"],
                StringComparer.Ordinal))
        {
            return Blocking(TemporalCarrier);
        }

        if (differingUrls.Length > 0 && differingUrls.All(ChoiceCastIndexUrls.Contains))
        {
            return Blocking(ChoiceCast);
        }

        return null;
    }

    private static ResourceParityClassification Blocking(string rootCause) =>
        new(rootCause, ParityReachability.SearchParameter, BlocksEnablement: true);

    private static ResourceParityClassification NonBlocking(string rootCause) =>
        new(rootCause, ParityReachability.LanguageConstruct, BlocksEnablement: false);

    private static bool ReturnedEmptyVersusValue(ParityDivergence divergence) =>
        !divergence.Firely.Threw
        && divergence.Firely.Results.Count == 0
        && !divergence.Ignixa.Threw
        && divergence.Ignixa.Results.Count == 1;

    private static bool FirelyRejected(ParityDivergence divergence) =>
        divergence.Firely.Threw
        && divergence.Firely.ExceptionType == nameof(ArgumentException)
        && !divergence.Ignixa.Threw;

    private static bool IsQuantityCollectionExpression(string expression) =>
        expression is "component.value.min()"
            or "component.value.max()"
            or "component.value.sum()"
            or "component.value.avg()"
            or "component.value.sort()";

    private static string IndexUrl(string entry)
    {
        int separator = entry.IndexOf('|', StringComparison.Ordinal);
        return separator < 0 ? entry : entry[..separator];
    }

    private static IReadOnlySet<string> ChoiceCastIndexUrls { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "http://hl7.org/fhir/SearchParameter/CommunicationRequest-occurrence",
            "http://hl7.org/fhir/SearchParameter/ConceptMap-source-uri",
            "http://hl7.org/fhir/SearchParameter/ConceptMap-target-uri",
            "http://hl7.org/fhir/SearchParameter/DeviceRequest-event-date",
            "http://hl7.org/fhir/SearchParameter/Goal-start-date",
            "http://hl7.org/fhir/SearchParameter/Goal-target-date",
            "http://hl7.org/fhir/SearchParameter/clinical-date",
            "http://hl7.org/fhir/SearchParameter/Observation-value-date",
            "http://hl7.org/fhir/SearchParameter/Observation-value-string",
            "http://hl7.org/fhir/SearchParameter/Observation-code-value-date",
        };
}
