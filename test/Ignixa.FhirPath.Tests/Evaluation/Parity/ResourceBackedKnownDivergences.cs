using Ignixa.Abstractions;
using Ignixa.Specification.ValueSets.Normative;

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
    /// which this corpus records but cannot adjudicate on its own.
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
    /// it.
    /// </para>
    /// <para>
    /// The adjudication therefore does not live here. <c>Ignixa.Search.Tests</c> holds a static
    /// registration census that compares Ignixa's converter registrations against a vendored snapshot
    /// of <c>microsoft/fhir-server</c>'s, and a composite component census that reads production's
    /// definition manager directly; between them every site below is either a documented divergence, a
    /// correct skip that upstream also performs, or a defect recorded against the layer that owns it.
    /// What remains here is the measurement: how far each site reaches across the corpus.
    /// </para>
    /// <para>
    /// The classes, by cause rather than by count:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>canonical</c> under 46 <c>Reference</c>-typed parameters (186 sites): the deliberate storage
    /// divergence tracked as #430. Ignixa registers <c>canonical</c> against <c>UriSearchValue</c>
    /// only, so those parameters index nothing until it is closed.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Backbone element types under a leaf-typed parameter - <c>Encounter.Location</c> (both as
    /// <c>FhirElementTypeNotSupported</c> and, once more, as <c>CannotInferSearchParamType</c> on the
    /// same site), <c>Ingredient.Manufacturer</c>, <c>MedicinalProductDefinition.Contact</c>,
    /// <c>SubstanceDefinition.Code</c>, <c>SubstanceDefinition.Name</c>,
    /// <c>SubstanceSpecification.Code</c>, and the two <c>SkippingElementNullOrEmptyInstanceType</c>
    /// sites (28 in total). These are not converter gaps. Every one is a path of the form
    /// <c>X.y.y</c> where a backbone's child shares the backbone's own name, and
    /// <c>SchemaAwareElement.Children</c> treats that as a recursive backbone and types the child as
    /// its parent. <c>Encounter.location.location</c> is a <c>Reference</c> in the schema and arrives
    /// as <c>Encounter.Location</c>, so the reference converter is never asked for. That is an
    /// <c>Ignixa.Serialization</c> element-model defect, not an indexing one, and it is tracked
    /// separately; the counts below are what will move when it is fixed.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Correct skips that upstream performs identically: <c>Attachment</c> and <c>base64Binary</c>
    /// under parameters that cannot represent them, <c>string</c> selected by a date parameter through
    /// a string-valued choice (<c>CarePlan.activity.detail.scheduledString</c>,
    /// <c>Procedure.performedString</c>), <c>uri</c> selected by a token parameter through
    /// <c>event[x]</c>, and <c>DeviceDefinition.udiDeviceIdentifier</c> selected by R6 ballot's
    /// <c>CanonicalResource-identifier</c>. No converter exists for any of these in either codebase.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>Location.Position</c> under <c>Location-near</c> and <c>Location-near-distance</c>: geo
    /// search is unimplemented, in Ignixa and upstream alike. The parameters index nothing.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>ComponentNullResolvedSearchParameter</c>: composites whose component definition URL the
    /// published HL7 package never publishes. The four STU3 <c>Observation-code-value-*</c> composites
    /// were in this class and are now repaired in <c>CompositeComponentDefinitionRepairs</c>, which is
    /// why 44 sites left this dictionary; the R5 and R6 remainder is recorded in
    /// <c>KnownCompositeComponentDivergences</c> with what upstream chose to do about each.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// Pinned exactly rather than floored, and by site rather than in total, because each of the three
    /// ways a number can move means something different. A new signature is a new unindexable site. A
    /// count that rises is an existing gap spreading. A count that falls is either a converter landing
    /// or a corpus that stopped generating the shape - and those two must not be indistinguishable.
    /// <see cref="UnconvertedPairs"/> is what separates them: it asserts against the live converter
    /// manager, so a converter landing reddens it by name while a corpus that stopped producing the
    /// shape leaves it green and moves only the count here. Read both failures together before
    /// re-pinning.
    /// </para>
    /// <para>
    /// The signatures carry the search parameter URL because production now logs it.
    /// <c>Log.FhirElementTypeNotSupported</c> used to record only the element type, which is why an
    /// earlier revision of this pin had thirteen anonymous <c>FhirElementTypeNotSupported</c> rows and
    /// a note explaining that the 46-parameter <c>canonical</c> breakdown had to be recovered by
    /// replaying the lookup outside the indexer. Anyone diagnosing this on a running server hit the
    /// same wall.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, int> ExpectedIgnixaConverterPipelineSkips { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["CannotInferSearchParamType :: http://hl7.org/fhir/SearchParameter/Encounter-location :: Encounter.Location :: "] = 1,
            ["ComponentNullResolvedSearchParameter :: code-value-string ::  :: "] = 11,
            ["ComponentNullResolvedSearchParameter :: progress-status-state-actual ::  :: "] = 2,
            ["ComponentNullResolvedSearchParameter :: progress-status-state-period ::  :: "] = 2,
            ["ComponentNullResolvedSearchParameter :: progress-status-state-period-actual ::  :: "] = 2,
            ["ComponentNullResolvedSearchParameter :: scope-artifact-conformance ::  :: "] = 1,
            ["ComponentNullResolvedSearchParameter :: scope-artifact-phase ::  :: "] = 1,
            ["ComponentNullResolvedSearchParameter :: specification-version ::  :: "] = 1,
            ["ComponentNullResolvedSearchParameter :: version-type ::  :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ActivityDefinition-depends-on :: canonical :: "] = 2,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/CanonicalResource-identifier :: DeviceDefinition.UdiDeviceIdentifier :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/CapabilityStatement-guide :: canonical :: "] = 7,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/CapabilityStatement-resource-profile :: canonical :: "] = 7,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/CapabilityStatement-supported-profile :: canonical :: "] = 11,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/CarePlan-activity-date :: string :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/CarePlan-instantiates-canonical :: canonical :: "] = 7,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/CodeSystem-supplements :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Communication-instantiates-canonical :: canonical :: "] = 6,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ConceptMap-other :: canonical :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ConceptMap-other-map :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ConceptMap-source :: canonical :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ConceptMap-source-group-system :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ConceptMap-source-scope :: canonical :: "] = 2,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ConceptMap-target :: canonical :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ConceptMap-target-group-system :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ConceptMap-target-scope :: canonical :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Consent-source :: Attachment :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Consent-source-reference :: Attachment :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Device-udi-carrier :: base64Binary :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/DeviceRequest-instantiates-canonical :: canonical :: "] = 5,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Encounter-location :: Encounter.Location :: "] = 8,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/FamilyMemberHistory-instantiates-canonical :: canonical :: "] = 6,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ImplementationGuide-depends-on :: canonical :: "] = 8,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ImplementationGuide-global :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Ingredient-manufacturer :: Ingredient.Manufacturer :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Location-near :: Location.Position :: "] = 5,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Location-near-distance :: Location.Position :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Measure-depends-on :: canonical :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/MeasureReport-measure :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/MedicinalProductDefinition-contact :: MedicinalProductDefinition.Contact :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/MessageDefinition-event :: uri :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/MessageDefinition-parent :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/MessageHeader-event :: canonical :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/MessageHeader-event :: uri :: "] = 2,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/MetadataResource-depends-on :: canonical :: "] = 9,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/NutritionOrder-instantiates-canonical :: canonical :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/OperationDefinition-base :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/OperationDefinition-input-profile :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/OperationDefinition-output-profile :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/PlanDefinition-definition :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/PlanDefinition-depends-on :: canonical :: "] = 2,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Procedure-instantiates-canonical :: canonical :: "] = 6,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/QuestionnaireResponse-questionnaire :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/RequestGroup-instantiates-canonical :: canonical :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/RequestOrchestration-instantiates-canonical :: canonical :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/RequestOrchestration-participant :: canonical :: "] = 5,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Requirements-actor :: canonical :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Requirements-derived-from :: canonical :: "] = 2,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ResearchDefinition-depends-on :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ResearchElementDefinition-depends-on :: canonical :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/SearchParameter-component :: canonical :: "] = 7,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/SearchParameter-derived-from :: canonical :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ServiceRequest-instantiates-canonical :: canonical :: "] = 6,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/StructureDefinition-base :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/SubstanceDefinition-code :: SubstanceDefinition.Code :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/SubstanceDefinition-name :: SubstanceDefinition.Name :: "] = 2,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/SubstanceSpecification-code :: SubstanceSpecification.Code :: "] = 2,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/TestReport-testscript :: canonical :: "] = 2,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/TestScript-artifact :: canonical :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact :: canonical :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/clinical-date :: string :: "] = 3,
            ["SkippingElementNullOrEmptyInstanceType :: http://hl7.org/fhir/SearchParameter/Encounter-location ::  :: "] = 2,
            ["SkippingElementNullOrEmptyInstanceType :: http://hl7.org/fhir/SearchParameter/InventoryReport-item ::  :: "] = 2,
        };

    /// <summary>
    /// The <c>(FHIR type, search parameter type)</c> pairs that must still resolve to no converter for
    /// the <c>FhirElementTypeNotSupported</c> rows of
    /// <see cref="ExpectedIgnixaConverterPipelineSkips"/> to mean what they say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the assertion that makes a shrinking pin readable. Asserted against the converter
    /// manager production builds, so it moves for exactly one reason - a converter landed - and it
    /// names which pair when it does. A count above that falls while every pair here still resolves to
    /// nothing is the other cause: the corpus stopped generating the shape, and coverage was lost
    /// rather than a gap closed.
    /// </para>
    /// <para>
    /// Every element type named by a <c>FhirElementTypeNotSupported</c> row appears here, which the
    /// corpus test checks, so a row cannot be deleted from the pin without also deleting the claim
    /// that its gap is still open.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(string FhirType, SearchParamType ParameterType)> UnconvertedPairs { get; } =
    [
        // The #430 canonical divergence: registered against UriSearchValue, never ReferenceSearchValue.
        ("canonical", SearchParamType.Reference),

        // Backbone types the element model hands to a leaf-typed parameter. Neither codebase has a
        // converter for a backbone, and neither should - the defect is upstream of the converter.
        ("Encounter.Location", SearchParamType.Reference),
        ("Ingredient.Manufacturer", SearchParamType.Reference),
        ("MedicinalProductDefinition.Contact", SearchParamType.Reference),
        ("SubstanceDefinition.Code", SearchParamType.Token),
        ("SubstanceDefinition.Name", SearchParamType.String),
        ("SubstanceSpecification.Code", SearchParamType.Token),
        ("DeviceDefinition.UdiDeviceIdentifier", SearchParamType.Token),

        // Correct skips: the element genuinely cannot be represented as the parameter's value type.
        ("Attachment", SearchParamType.Reference),
        ("base64Binary", SearchParamType.String),
        ("string", SearchParamType.Date),
        ("uri", SearchParamType.Token),

        // Geo search, unimplemented here and upstream. Location-near is Token under STU3 and Special
        // from R4 onward; Location-near-distance is Quantity.
        ("Location.Position", SearchParamType.Token),
        ("Location.Position", SearchParamType.Special),
        ("Location.Position", SearchParamType.Quantity),
    ];

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
    /// side growing while the other collapses. The two currently sit at 10,745 Firely and 10,756
    /// Ignixa - the eleven-entry gap is the 11 divergent resources - so the floor is the lower of them.
    /// Raised from 10,743 when repairing the STU3 Observation composite component references let both
    /// indexers emit composites they had both been dropping.
    /// </para>
    /// </remarks>
    public const int MinimumIndexEntriesComparedPerEngine = 10745;

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
