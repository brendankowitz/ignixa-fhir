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
    /// Every failure production <c>ElementSearchIndexer</c> contains during the index sweep, keyed by
    /// site and counted by reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These were invisible until the harness stopped handing the production indexer a null logger
    /// factory. The indexer catches evaluation and conversion failures per search parameter, logs them
    /// and continues - correct for a write path, fatal for a differential harness, because a parameter
    /// that throws contributes no entries and an entry-list comparison then scores Ignixa's failure
    /// against Firely's legitimate empty as agreement.
    /// </para>
    /// <para>
    /// None of these is a divergence: the index sweep still reports the same 11 divergent resources it
    /// did before they became visible, because the reference indexer performs the matching skips - it
    /// breaks out of a composite whose component definition is unresolved, and it <c>continue</c>s past
    /// an element type it has no converter for, exactly as production does. What they establish is that
    /// 302 of the sweep's comparisons are backed by mutual silence rather than by matched values, which
    /// is the thing an entry-list equality cannot say.
    /// </para>
    /// <para>
    /// Pinned exactly rather than floored, and by site rather than in total, because each of the three
    /// ways this number can move means something different. A new signature is a new unindexable site.
    /// A count that rises is an existing gap spreading. A count that falls is either a real fix or a
    /// corpus that stopped generating the shape - and the two must not be indistinguishable. The one
    /// entry that is an actual thrown exception rather than a classification skip is the
    /// <c>NotSupportedException</c> from <c>hasExtension()</c>, which <see cref="KnownDivergences"/>
    /// already pins on the Select side.
    /// </para>
    /// <para>
    /// <c>FhirElementTypeNotSupported</c> signatures carry no parameter identity because production
    /// logs only the element type for that event. That is a gap in production logging; it is recorded
    /// here rather than worked around, since inventing an identity the log does not carry would make
    /// the pin say more than the evidence does.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, int> ExpectedIgnixaIndexFailures { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["CannotInferSearchParamType :: http://hl7.org/fhir/SearchParameter/Encounter-location :: Encounter.Location :: "] = 1,
            ["ComponentNullResolvedSearchParameter :: code-value-concept ::  :: "] = 11,
            ["ComponentNullResolvedSearchParameter :: code-value-date ::  :: "] = 11,
            ["ComponentNullResolvedSearchParameter :: code-value-quantity ::  :: "] = 11,
            ["ComponentNullResolvedSearchParameter :: code-value-string ::  :: "] = 22,
            ["ComponentNullResolvedSearchParameter :: progress-status-state-actual ::  :: "] = 2,
            ["ComponentNullResolvedSearchParameter :: progress-status-state-period ::  :: "] = 2,
            ["ComponentNullResolvedSearchParameter :: progress-status-state-period-actual ::  :: "] = 2,
            ["ComponentNullResolvedSearchParameter :: scope-artifact-conformance ::  :: "] = 1,
            ["ComponentNullResolvedSearchParameter :: scope-artifact-phase ::  :: "] = 1,
            ["ComponentNullResolvedSearchParameter :: specification-version ::  :: "] = 1,
            ["ComponentNullResolvedSearchParameter :: version-type ::  :: "] = 3,
            ["FailedToExtractValues :: http://hl7.org/fhir/SearchParameter/questionnaireresponse-extensions-QuestionnaireResponse-item-subject :: QuestionnaireResponse :: NotSupportedException"] = 1,
            ["FhirElementTypeNotSupported ::  :: Attachment :: "] = 2,
            ["FhirElementTypeNotSupported ::  :: DeviceDefinition.UdiDeviceIdentifier :: "] = 1,
            ["FhirElementTypeNotSupported ::  :: Encounter.Location :: "] = 8,
            ["FhirElementTypeNotSupported ::  :: Ingredient.Manufacturer :: "] = 4,
            ["FhirElementTypeNotSupported ::  :: Location.Position :: "] = 6,
            ["FhirElementTypeNotSupported ::  :: MedicinalProductDefinition.Contact :: "] = 4,
            ["FhirElementTypeNotSupported ::  :: SubstanceDefinition.Code :: "] = 3,
            ["FhirElementTypeNotSupported ::  :: SubstanceDefinition.Name :: "] = 2,
            ["FhirElementTypeNotSupported ::  :: SubstanceSpecification.Code :: "] = 2,
            ["FhirElementTypeNotSupported ::  :: base64Binary :: "] = 1,
            ["FhirElementTypeNotSupported ::  :: canonical :: "] = 186,
            ["FhirElementTypeNotSupported ::  :: string :: "] = 7,
            ["FhirElementTypeNotSupported ::  :: uri :: "] = 3,
            ["SkippingElementNullOrEmptyInstanceType :: http://hl7.org/fhir/SearchParameter/Encounter-location ::  :: "] = 2,
            ["SkippingElementNullOrEmptyInstanceType :: http://hl7.org/fhir/SearchParameter/InventoryReport-item ::  :: "] = 2,
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
    /// <para>
    /// This is legitimate agreement - a search parameter expression that matches nothing on a resource
    /// is the common case - but it is agreement on absence, so it is much weaker evidence than a
    /// matched value and it is invisible to every divergence-based assertion. Pinning it exactly means
    /// the composition of the sweep cannot drift silently: 9,453 of 19,647 evaluations agree on empty;
    /// the remaining 10,194 compare real values, of which 10,074 agree and 120 are the pinned
    /// divergences. If an engine change moves this number the pin has to be updated deliberately, with
    /// the shift understood, rather than absorbed into an unchanged divergence count.
    /// </para>
    /// <para>
    /// Most of what this pin measures is the corpus, not either engine. Of the 9,453, 5,888 (62.3%)
    /// come from 311 expressions that are never non-empty anywhere in the sweep -
    /// <c>Resource.meta.profile</c>, <c>Resource.meta.security</c> and <c>Resource.meta.tag</c> at 788
    /// each, <c>Resource.meta.source</c> at 658, <c>Resource.language</c> at 345 - and only 3,565
    /// (37.7%) come from expressions that do produce values somewhere. The likeliest cause of this
    /// number moving is therefore a corpus or <c>SchemaBasedFhirResourceFaker</c> density change, which
    /// says nothing about conformance: check that direction first, and re-pin only once you can say
    /// which of the two moved. <see cref="MinimumAgreementsOnValues"/>, not this, is the assertion that
    /// carries the conformance claim.
    /// </para>
    /// </remarks>
    public const int ExpectedBothEmpty = 9453;

    /// <summary>
    /// Lower bound on evaluations where both engines returned the same non-empty results - the only
    /// bucket that is positive evidence the two agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ExpectedBothThrew"/> and <see cref="ExpectedBothEmpty"/> pin the agreements that
    /// establish nothing, and <see cref="ExpectedSelectCounts"/> pins the disagreements. None of them
    /// floors the evidence base, so today's 10,074 value agreements could halve, <see cref="ExpectedBothEmpty"/>
    /// could be raised to absorb the difference, and every other assertion here would still pass on half
    /// the evidence.
    /// </para>
    /// <para>
    /// A floor rather than an exact pin, because the two are not equally safe to update. An exact pin is
    /// satisfied by any number that has been written down, so losing evidence and gaining it both look
    /// like a re-pin and neither prompts a question. A floor can only be satisfied by holding or gaining
    /// evidence: raise it when the sweep genuinely covers more, and never lower it to accommodate a
    /// regression, because a number below this one is the finding rather than the maintenance.
    /// </para>
    /// </remarks>
    public const int MinimumAgreementsOnValues = 10074;

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
