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
    /// The adjudication therefore does not live here. <c>Ignixa.Search.Tests</c> holds a converter
    /// registration census against a vendored <c>microsoft/fhir-server</c> snapshot and a composite
    /// component census over production's definition manager; between them every site below is a
    /// documented divergence, a correct skip upstream also performs, or a defect recorded against the
    /// layer that owns it. What remains here is the measurement: how far each site reaches.
    /// </para>
    /// <para>
    /// The classes, by cause:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>canonical</c> under 46 parameters (186 sites) - 45 <c>Reference</c>-typed plus
    /// <c>MessageHeader-event</c>, which is <c>Token</c>: the deliberate storage
    /// divergence tracked as #430. Ignixa registers <c>canonical</c> against <c>UriSearchValue</c> only.
    /// </description></item>
    /// <item><description>
    /// Backbone element types under a leaf-typed parameter used to cost 28 sites here (issue #454): each
    /// was a path <c>X.y.y</c> where a backbone's child shared the backbone's own name, and
    /// <c>SchemaAwareElement.Children</c>'s recursion heuristic - keyed on name equality alone - typed the
    /// child as its parent regardless of what the schema actually declared, so
    /// <c>Encounter.location.location</c>, a <c>Reference</c> in the schema, arrived as
    /// <c>Encounter.Location</c> and the reference converter was never asked for. Narrowing the heuristic
    /// to also require a schema-declared <c>ContentReference</c> fixed 27 of the 28 and is why those rows
    /// left this dictionary. The 28th, <c>Ingredient-manufacturer</c>, drops from 4 to 1 rather than
    /// vanishing: R4B's own published <c>SearchParameter</c> expression is <c>Ingredient.manufacturer</c>
    /// (the backbone itself, one level short of the nested <c>Reference</c> that R5 and R6 point at), a
    /// gap in the published definition rather than in the element model, and the element-model fix now
    /// lets that shallower expression actually reach the indexer instead of being masked by the deeper
    /// mistyping. Fixing the heuristic also unmasked a second, previously-unreachable gap, already known
    /// to <c>KnownCompositeComponentDivergences</c>: R5's <c>Encounter-location-period</c> composite's
    /// first component evaluates <c>location.reference</c> against the same nested element, which used to
    /// resolve empty and skip the component loop before it ever reached the composite's second component -
    /// <c>Encounter-period</c>, dropped from R5 when <c>Encounter.period</c> was renamed to
    /// <c>Encounter.actualPeriod</c>, so its definition URL dangles in the published package itself. That
    /// pre-existing, adjudicated gap is the new <c>ComponentNullResolvedSearchParameter :: location-period</c>
    /// row below - reachable, not created, by this fix.
    /// </description></item>
    /// <item><description>
    /// Correct skips upstream performs identically: <c>Attachment</c> and <c>base64Binary</c> under
    /// parameters that cannot represent them, <c>string</c> under a date parameter, <c>uri</c> under a
    /// token parameter, <c>DeviceDefinition.udiDeviceIdentifier</c> under R6 ballot's
    /// <c>CanonicalResource-identifier</c>, and <c>Location.Position</c> under <c>Location-near</c>,
    /// where geo search is unimplemented here and upstream alike.
    /// </description></item>
    /// <item><description>
    /// <c>ComponentNullResolvedSearchParameter</c>: composites whose component definition URL the
    /// published HL7 package never publishes. The four STU3 <c>Observation-code-value-*</c> composites are
    /// now repaired in <c>CompositeComponentDefinitionRepairs</c>, which is why 44 sites left this
    /// dictionary; the R5 and R6 remainder is in <c>KnownCompositeComponentDivergences</c>.
    /// </description></item>
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
    /// The signatures carry the search parameter URL because production now logs it;
    /// <c>Log.FhirElementTypeNotSupported</c> used to record only the element type, so the 46-parameter
    /// <c>canonical</c> breakdown had to be recovered by replaying the lookup outside the indexer - as
    /// would anyone diagnosing this on a running server.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, int> ExpectedIgnixaConverterPipelineSkips { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ComponentNullResolvedSearchParameter :: code-value-string ::  :: "] = 11,
            ["ComponentNullResolvedSearchParameter :: location-period ::  :: "] = 2,
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
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/FamilyMemberHistory-instantiates-canonical :: canonical :: "] = 6,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ImplementationGuide-depends-on :: canonical :: "] = 8,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/ImplementationGuide-global :: canonical :: "] = 4,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Ingredient-manufacturer :: Ingredient.Manufacturer :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Location-near :: Location.Position :: "] = 5,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Location-near-distance :: Location.Position :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/Measure-depends-on :: canonical :: "] = 3,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/MeasureReport-measure :: canonical :: "] = 4,
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
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/TestReport-testscript :: canonical :: "] = 2,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/TestScript-artifact :: canonical :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact :: canonical :: "] = 1,
            ["FhirElementTypeNotSupported :: http://hl7.org/fhir/SearchParameter/clinical-date :: string :: "] = 3,
        };

    /// <summary>
    /// The <c>(FHIR type, search parameter type)</c> pairs that must still resolve to no converter for
    /// the <c>FhirElementTypeNotSupported</c> rows of
    /// <see cref="ExpectedIgnixaConverterPipelineSkips"/> to mean what they say.
    /// </summary>
    /// <remarks>
    /// This is the assertion that makes a shrinking pin readable. Asserted against the converter manager
    /// production builds, so it moves for exactly one reason - a converter landed - and names which pair.
    /// A count in <see cref="ExpectedIgnixaConverterPipelineSkips"/> that falls while every pair here still
    /// resolves to nothing is the other cause: the corpus stopped generating the shape, so coverage was
    /// lost rather than a gap closed. The corpus test checks that every element type named by a
    /// <c>FhirElementTypeNotSupported</c> row appears here, so a row cannot be deleted from the pin without
    /// also deleting the claim that its gap is still open.
    /// </remarks>
    public static IReadOnlyList<(string FhirType, SearchParamType ParameterType)> UnconvertedPairs { get; } =
    [
        // The #430 canonical divergence: registered against UriSearchValue, never ReferenceSearchValue.
        ("canonical", SearchParamType.Reference),

        // MessageHeader-event is Token and its R6 element is a canonical, so this is a second canonical
        // gap rather than a case of the one above. The check that reads this list matches on the parameter
        // type as well as the element type, so closing one cannot silently make the other's row vanish.
        ("canonical", SearchParamType.Token),

        // Backbone types the element model hands to a leaf-typed parameter. Neither codebase has a
        // converter for a backbone, and neither should - the defect is upstream of the converter.
        // Encounter.Location, MedicinalProductDefinition.Contact, SubstanceDefinition.Code/Name and
        // SubstanceSpecification.Code left this list with issue #454: the element model now types their
        // sole name-equality site as the schema-declared leaf - these were never recursion sites, that
        // they were treated as recursion is the defect - so a backbone is never handed to the converter
        // for them again. Ingredient.Manufacturer stays - not because the element model still mistypes it,
        // but because R4B's own published Ingredient-manufacturer expression names the backbone itself.
        ("Ingredient.Manufacturer", SearchParamType.Reference),
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
    /// The index half asserted that its divergences were classified and its failure sites pinned; neither
    /// says how much was compared, so halving the entries every parameter produced satisfied both. A
    /// floor rather than an exact pin, for the reason on <see cref="MinimumAgreementsOnValues"/>. Applied
    /// per engine rather than to the total, because a total is satisfied by one side growing while the
    /// other collapses: the two sit at 10,777 Firely and 10,788 Ignixa - the gap is the 11 divergent
    /// resources - so the floor is the lower. Raised from 10,745 by issue #454's
    /// <c>SchemaAwareElement</c> recursion-heuristic fix: both engines gained exactly the same 32
    /// entries, because both indexers share the one production element model the fix corrected -
    /// <c>Encounter.location.location</c> and its false-positive siblings now arrive typed as their
    /// schema-declared leaf instead of their parent backbone, so the parameters that target them
    /// contribute entries neither engine could produce before. A schema walk across all five generated
    /// providers counts 37 unique false-positive qualified paths over 112 site-instances, in two
    /// classes: 33 paths / 93 sites under a backbone parent, which is what this corpus measures, and
    /// four under a datatype parent - <c>Reference.reference</c>, <c>Expression.expression</c>,
    /// <c>id.id</c> and <c>Extension.extension</c> - which it does not. That second class is the wider
    /// one by far: <c>Reference.reference</c> is on every reference in every resource, and it is why
    /// <c>ResolveFunctionTests</c> re-pins from <c>Reference</c> to <c>string</c>. It moves no count
    /// here because no search parameter resolves a reference's own <c>reference</c> child. The
    /// follow-up widening in <c>SchemaAwareElement.ComputeChildResolution</c> that resolves a
    /// <c>ContentReference</c> to its actual target, rather than only the 19 paths where the child's
    /// name also matched the parent backbone's last segment, measured zero further movement here: none
    /// of the 76 additional qualified paths it fixes are reached by a search
    /// parameter this corpus exercises or by a shape <c>SchemaBasedFhirResourceFaker</c> generates deeply
    /// enough to trigger, so this floor is unchanged by that half of the work. Previously raised from
    /// 10,743 when repairing the STU3 Observation composite component references let both indexers emit
    /// composites they had both been dropping.
    /// </remarks>
    public const int MinimumIndexEntriesComparedPerEngine = 10777;

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
    /// Legitimate agreement, but agreement on absence: much weaker evidence than a matched value, and
    /// invisible to every divergence-based assertion. Pinned exactly so the composition of the sweep cannot
    /// drift silently - 9,453 of 19,647 evaluations agree on empty; the remaining 10,194 compare real
    /// values, of which 10,074 agree and 120 are the pinned divergences.
    /// </para>
    /// <para>
    /// Most of what this measures is the corpus, not either engine: 5,888 of the 9,453 come from 311
    /// expressions that are never non-empty anywhere in the sweep (<c>Resource.meta.profile</c>,
    /// <c>.security</c> and <c>.tag</c> at 788 each, <c>.source</c> at 658, <c>Resource.language</c> at
    /// 345). A corpus or <c>SchemaBasedFhirResourceFaker</c> density change is therefore the likeliest
    /// cause of this number moving, and says nothing about conformance - check that direction first and
    /// re-pin only once you can say which of the two moved. <see cref="MinimumAgreementsOnValues"/>
    /// carries the conformance claim, not this.
    /// </para>
    /// </remarks>
    public const int ExpectedBothEmpty = 9453;

    /// <summary>
    /// Lower bound on evaluations where both engines returned the same non-empty results - the only
    /// bucket that is positive evidence the two agree.
    /// </summary>
    /// <remarks>
    /// The other pins do not floor the evidence base: the value agreements could halve,
    /// <see cref="ExpectedBothEmpty"/> be raised to absorb the difference, and every other assertion still
    /// pass on half the evidence. A floor rather than an exact pin because an exact pin is satisfied by any
    /// number that has been written down, so losing evidence and gaining it look alike; a floor can only be
    /// satisfied by holding or gaining evidence. Raise it when the sweep genuinely covers more; never lower
    /// it to accommodate a regression.
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
