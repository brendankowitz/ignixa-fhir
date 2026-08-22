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
    /// The failures production <c>ElementSearchIndexer</c> contained on the one axis this harness
    /// actually evaluates twice: FHIRPath evaluation and value conversion.
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
    /// A contained throw is what this dictionary holds, and it is separated from
    /// <see cref="ExpectedIgnixaConverterPipelineSkips"/> because only these are corroborated. Ignixa's
    /// evaluator ran the expression and Firely's ran it too, so a failure here is a real observation
    /// about two engines. Everything in the other dictionary is Ignixa's own code reached from both
    /// sides, where agreement establishes nothing.
    /// </para>
    /// <para>
    /// The single entry is the <c>NotSupportedException</c> Ignixa raises for <c>hasExtension()</c>,
    /// which <see cref="KnownDivergences"/> already pins on the Select side - Firely refuses the same
    /// parameter at compile time, so the expression is outside the compared set and neither engine
    /// indexes it.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, int> ExpectedIgnixaEvaluationFailures { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["FailedToExtractValues :: http://hl7.org/fhir/SearchParameter/questionnaireresponse-extensions-QuestionnaireResponse-item-subject :: QuestionnaireResponse :: NotSupportedException"] = 1,
        };

    /// <summary>
    /// The elements production <c>ElementSearchIndexer</c> classified as unindexable and skipped,
    /// which this corpus records but cannot adjudicate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read the limitation before reading the numbers. This is a FHIRPath harness: the only thing it
    /// runs on two engines is <c>Select</c>. Everything downstream of that - the search parameter
    /// definitions, <c>InferSearchParamTypeFromFhirType</c>,
    /// <c>GetSearchValueTypeForSearchParamType</c> and the converter manager - is a single set of
    /// Ignixa objects that <see cref="SearchIndexParityHarness"/> constructs once and hands to
    /// <em>both</em> indexers. The reference indexer resolves converters through the same instance
    /// production does. So when both sides skip an element that is one object making one decision, not
    /// two implementations agreeing, and no entry-list comparison over this corpus can detect a gap in
    /// it. An earlier revision of this comment claimed the reference indexer performs the matching
    /// skips "exactly as production does" and offered that as corroboration. It is a structural
    /// guarantee, and it was false as a claim about agreement.
    /// </para>
    /// <para>
    /// What the skips are, measured by replaying the converter lookup with the parameter identity
    /// production does not log: 229 of the 301 are <c>FhirElementTypeNotSupported</c>, a converter
    /// manager miss, and 186 of those are <c>canonical</c> under 46 shipped SearchParameters - 45
    /// <c>Reference</c>-typed plus <c>MessageHeader-event</c>. Ignixa registers <c>canonical</c>
    /// against <c>UriSearchValue</c> only, so those 46 parameters index nothing. Among them are
    /// <c>QuestionnaireResponse-questionnaire</c>, <c>MeasureReport-measure</c>,
    /// <c>StructureDefinition-base</c>, <c>PlanDefinition-definition</c>, the
    /// <c>instantiates-canonical</c> family across nine resource types, the <c>-depends-on</c> family,
    /// and eight <c>ConceptMap</c> parameters.
    /// </para>
    /// <para>
    /// microsoft/fhir-server, which this indexer was ported from, additionally ships
    /// <c>CanonicalToReferenceSearchValueConverter</c>, <c>IdToReferenceSearchValueConverter</c>,
    /// <c>IdentifierToStringSearchValueConverter</c> and <c>ReferenceToUriSearchValueConverter</c>.
    /// The first is what closes the 186. Writing them is <c>Ignixa.Search</c> production work, tracked
    /// separately as release-blocking and deliberately out of scope for a FHIRPath change; what
    /// belongs here is that the corpus stops implying it has cleared them. When the converters land
    /// these counts drop, and this pin is what says by how much.
    /// </para>
    /// <para>
    /// Pinned exactly rather than floored, and by site rather than in total, because each of the three
    /// ways a number can move means something different. A new signature is a new unindexable site. A
    /// count that rises is an existing gap spreading. A count that falls is either a converter landing
    /// or a corpus that stopped generating the shape - and the two must not be indistinguishable.
    /// </para>
    /// <para>
    /// <c>FhirElementTypeNotSupported</c> signatures carry no parameter identity because production
    /// logs only the element type for that event. That is a gap in production logging; it is recorded
    /// here rather than worked around, since inventing an identity the log does not carry would make
    /// the pin say more than the evidence does. The 46-parameter breakdown above was recovered by
    /// replaying the lookup outside the indexer, not read out of these signatures.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, int> ExpectedIgnixaConverterPipelineSkips { get; } =
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
    /// Lower bound on the canonicalised index entries each engine contributes across the whole index
    /// sweep.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The index half asserted that its divergences were classified and its failure sites pinned.
    /// Neither says how much was compared, so a change that halved the entries every parameter
    /// produced satisfied both - the same defect class as an agreement count derived by subtraction,
    /// one level up and in the half that runs the production indexer over every resource in the
    /// corpus. A floor rather than an exact pin, for the reason given on
    /// <see cref="MinimumAgreementsOnValues"/>: it can only be satisfied by holding or gaining
    /// evidence.
    /// </para>
    /// <para>
    /// Applied to each engine separately rather than to the total, because a total is satisfied by one
    /// side growing while the other collapses. The two currently sit at 10,743 Firely and 10,753
    /// Ignixa - the ten-entry gap is the 11 divergent resources - so the floor is the lower of them.
    /// </para>
    /// </remarks>
    public const int MinimumIndexEntriesComparedPerEngine = 10743;

    /// <summary>
    /// Lower bound on resources reaching the index sweep, so a corpus that stopped generating them
    /// cannot satisfy the index pins with nothing to index.
    /// </summary>
    public const int MinimumIndexResourceCount = 788;

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
