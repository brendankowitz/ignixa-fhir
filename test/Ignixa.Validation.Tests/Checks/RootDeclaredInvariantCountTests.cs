// <copyright file="RootDeclaredInvariantCountTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Reflection;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.Specification.Generated;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Pins how many constraint entries the generated schema providers carry on TYPE ROOT nodes - the
/// <c>ElementDefinition</c> row whose path is the bare type name (e.g. "Patient", "Quantity"), as
/// opposed to a synthesized BackboneElement row (e.g. "Patient.Contact"). A backbone row always carries
/// <c>constraints: null</c> by design: <c>GetRootElementRow</c> in
/// <c>codegen/Ignixa.Specification.Generators/CSharpCoreSchemaLanguage.cs</c> returns null whenever a
/// backbone path is supplied, because a backbone's own constraints are emitted once already, on the
/// child element row that declares it (e.g. <c>tim-*</c> on the <c>Timing.repeat</c> child row, not on
/// the synthesized <c>Timing.Repeat</c> type node) - emitting them again here would double-evaluate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test exists.</b> <see cref="RootDeclaredInvariantTests"/> proves five individual root
/// constraints fire correctly against hand-built instances (dom-2, dom-6, bdl-1, rng-2, qty-3). It says
/// nothing about the other several hundred root constraint entries each FHIR version ships. Before the
/// fix in commit b65a6941, the generator emitted a hard-coded <c>constraints: null</c> literal for
/// EVERY type root node, in every version, and nothing in the suite noticed - a validator that silently
/// runs fewer checks does not fail, it just reports fewer issues, which looks identical to "the
/// resource is more valid than we thought." A regression that reintroduces that bug for even one FHIR
/// version - or for a handful of resource types within one version - would leave every existing green
/// test exactly as green as before. This test enumerates <see cref="ITypeExtended.Constraints"/>
/// directly off each generated provider's type-root nodes so that kind of silent drop is caught by a
/// failing count instead of by nobody.
/// </para>
/// <para>
/// <b>These counts do not match the "237" figure quoted in <see cref="RootDeclaredInvariantTests"/>'s
/// remarks (also 239/312/320/186 for R4B/R5/R6/STU3, and the "99 shipped before" figures) - and that
/// discrepancy was checked, not assumed away.</b> Measured by direct reflection over each generated
/// provider's private <c>_types</c> dictionary (the same data <see
/// cref="Ignixa.Validation.Schema.StructureDefinitionSchemaBuilder"/> reads when it builds a resource's
/// validation schema), the real totals are R4 920, R4B 903, R5 861, R6 872, STU3 601 - three to four
/// times the prose figures. The gap has a concrete cause: the generator reads each type's own SNAPSHOT
/// root row (<c>GetRootElementRow</c> reads <c>sd.Snapshot</c>, not <c>sd.Differential</c>), and a
/// StructureDefinition's snapshot is fully expanded, so a universal invariant is copied verbatim onto
/// the root row of every inheriting type - it is not declared once on "DomainResource" and left there.
/// Verified directly against the shipped R4 provider: dom-2..dom-6 each appear on 144 separate resource
/// root nodes (720 of R4's 920 entries, one full copy per DomainResource-derived resource - confirmed
/// by inspecting <c>R4CoreSchemaProvider.g.cs</c>'s own "Account" entry, which carries all five), and
/// ele-1 appears as a root-scope entry on 61 more. The "237" prose figure is much closer to
/// R4's 141 DISTINCT root-scope constraint keys, but does not match that either, so it most likely
/// describes a differential-only ("genuinely new at this type, not inherited") audit computed against
/// the raw FHIR package rather than anything queryable from these generated providers. That number may
/// well be correct for ITS definition of "declared" - it is simply answering a different question than
/// "how many constraint entries does the shipped schema provider actually carry," which is what this
/// test pins. The prose in <see cref="RootDeclaredInvariantTests"/> should be read with that caveat.
/// </para>
/// <para>
/// <b>Exact pin, not a lower bound.</b> A lower bound (<c>ShouldBeGreaterThanOrEqualTo</c>) would never
/// fail on a partial regression that keeps the total above the floor - e.g. losing dom-2..dom-6 from
/// forty resource types while a legitimate spec bump adds just as many entries elsewhere nets out to
/// "still above the bound," even though real coverage was lost. That is precisely the failure mode this
/// test exists to catch: the original bug was a silent, total loss of ALL root constraints, and nothing
/// about a silent PARTIAL loss looks different from the suite's point of view. A legitimate FHIR version
/// bump (a new patch release of R4B, a ballot update to R6) is rare, deliberate, and always paired with
/// regenerating these files and running the full suite - at which point this test fails loudly, and the
/// failure message below (constraint-key diff, not just two integers) makes updating the pin a five
/// minute review, not a guess. That trade - occasional deliberate pin updates in exchange for catching
/// silent drops - is the right one for a generator output nothing else in the suite verifies at this
/// granularity.
/// </para>
/// </remarks>
public class RootDeclaredInvariantCountTests
{
    [Fact]
    public void GivenR4GeneratedProvider_WhenEnumeratingRootDeclaredConstraints_ThenCountAndBreakdownMatchPinnedSnapshot()
        => AssertMatchesSnapshot("R4", typeof(R4CoreSchemaProvider), 920, R4Snapshot);

    [Fact]
    public void GivenR4BGeneratedProvider_WhenEnumeratingRootDeclaredConstraints_ThenCountAndBreakdownMatchPinnedSnapshot()
        => AssertMatchesSnapshot("R4B", typeof(R4BCoreSchemaProvider), 903, R4BSnapshot);

    [Fact]
    public void GivenR5GeneratedProvider_WhenEnumeratingRootDeclaredConstraints_ThenCountAndBreakdownMatchPinnedSnapshot()
        => AssertMatchesSnapshot("R5", typeof(R5CoreSchemaProvider), 861, R5Snapshot);

    [Fact]
    public void GivenR6GeneratedProvider_WhenEnumeratingRootDeclaredConstraints_ThenCountAndBreakdownMatchPinnedSnapshot()
        => AssertMatchesSnapshot("R6", typeof(R6CoreSchemaProvider), 872, R6Snapshot);

    [Fact]
    public void GivenSTU3GeneratedProvider_WhenEnumeratingRootDeclaredConstraints_ThenCountAndBreakdownMatchPinnedSnapshot()
        => AssertMatchesSnapshot("STU3", typeof(STU3CoreSchemaProvider), 601, STU3Snapshot);

    /// <summary>
    /// Measures the actual root-declared constraint breakdown off a generated provider and compares it
    /// against the pinned expected total and per-type key snapshot, failing with a diff that names
    /// exactly which types and constraint keys changed rather than two bare integers.
    /// </summary>
    private static void AssertMatchesSnapshot(string version, Type providerType, int expectedTotal, string expectedSnapshotText)
    {
        // Arrange
        var expectedByType = ParseSnapshot(expectedSnapshotText);
        var expectedFromSnapshot = expectedByType.Values.Sum(keys => keys.Length);
        expectedFromSnapshot.ShouldBe(
            expectedTotal,
            $"{version}: the pinned total ({expectedTotal}) and the pinned per-type snapshot " +
            $"(sums to {expectedFromSnapshot}) have drifted apart - fix the test data, not the assertion.");

        // Act
        var (actualTotal, actualByType) = MeasureRootDeclaredConstraints(providerType);

        // Assert
        var matches = actualTotal == expectedTotal && SnapshotsEqual(expectedByType, actualByType);
        matches.ShouldBeTrue(DescribeMismatch(version, expectedTotal, actualTotal, expectedByType, actualByType));
    }

    /// <summary>
    /// Enumerates every TYPE ROOT node (a key in the provider's internal type table that carries no
    /// dot - a BackboneElement row like "Patient.Contact" always has a dot and is excluded, matching
    /// exactly what the generator's <c>GetRootElementRow</c> treats as "root") and sums the constraint
    /// keys declared directly on it.
    /// </summary>
    private static (int Total, IReadOnlyDictionary<string, string[]> ByType) MeasureRootDeclaredConstraints(Type providerType)
    {
        var typesField = providerType.GetField("_types", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"{providerType.Name} no longer has a private static '_types' field - the generator's " +
                "internal layout changed. Update this test's reflection target (and re-verify every " +
                "pinned count below, since the whole measurement depends on it) together with whatever " +
                "changed it.");

        var allTypes = (Dictionary<string, IType>)typesField.GetValue(null)!;

        var byType = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var total = 0;
        foreach (var (name, type) in allTypes)
        {
            // BackboneElement rows (e.g. "Patient.Contact") are excluded: the generator deliberately
            // never populates their Constraints (see GetRootElementRow's remarks), so this mirrors
            // exactly what "root-declared" means for the generated schema.
            if (name.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            if (type is not ITypeExtended extended || extended.Constraints.Count == 0)
            {
                continue;
            }

            var keys = extended.Constraints.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            byType[name] = keys;
            total += keys.Length;
        }

        return (total, byType);
    }

    private static bool SnapshotsEqual(
        IReadOnlyDictionary<string, string[]> expected,
        IReadOnlyDictionary<string, string[]> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        foreach (var (type, expectedKeys) in expected)
        {
            if (!actual.TryGetValue(type, out var actualKeys) || !expectedKeys.SequenceEqual(actualKeys, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds a failure message that names WHAT changed, not just that the numbers disagree: which
    /// types lost every root constraint, which gained root constraints for the first time, and which
    /// kept some constraints but with a different key set (a partial drop or addition).
    /// </summary>
    private static string DescribeMismatch(
        string version,
        int expectedTotal,
        int actualTotal,
        IReadOnlyDictionary<string, string[]> expected,
        IReadOnlyDictionary<string, string[]> actual)
    {
        var delta = actualTotal - expectedTotal;
        var sb = new StringBuilder();
        sb.Append(version)
            .Append(": expected ")
            .Append(expectedTotal)
            .Append(" root-declared constraint entries across ")
            .Append(expected.Count)
            .Append(" root type nodes, found ")
            .Append(actualTotal)
            .Append(" across ")
            .Append(actual.Count)
            .Append(" (")
            .Append(delta >= 0 ? "+" : string.Empty)
            .Append(delta)
            .Append(").");

        if (actualTotal == 0 && expectedTotal > 0)
        {
            sb.Append(" EVERY root type lost its constraints - this is the exact shape of the original " +
                "bug (generator emitting 'constraints: null' for every type root row again).");
        }

        var removedTypes = expected.Keys.Except(actual.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var addedTypes = actual.Keys.Except(expected.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var changedTypes = expected.Keys
            .Intersect(actual.Keys, StringComparer.Ordinal)
            .Where(type => !expected[type].SequenceEqual(actual[type], StringComparer.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        AppendTypeList(sb, "Types that LOST all root constraints", removedTypes);
        AppendTypeList(sb, "Types that GAINED root constraints (new type root, or first constraint)", addedTypes);

        foreach (var type in changedTypes.Take(25))
        {
            var removedKeys = expected[type].Except(actual[type], StringComparer.Ordinal).ToArray();
            var addedKeys = actual[type].Except(expected[type], StringComparer.Ordinal).ToArray();
            sb.Append("\n  ").Append(type).Append(": expected [").Append(string.Join(",", expected[type]))
                .Append("], found [").Append(string.Join(",", actual[type])).Append(']');
            if (removedKeys.Length > 0)
            {
                sb.Append(" - lost: ").Append(string.Join(",", removedKeys));
            }

            if (addedKeys.Length > 0)
            {
                sb.Append(" - gained: ").Append(string.Join(",", addedKeys));
            }
        }

        if (changedTypes.Count > 25)
        {
            sb.Append("\n  ... and ").Append(changedTypes.Count - 25).Append(" more types with a changed key set.");
        }

        if (removedTypes.Count == 0 && addedTypes.Count == 0 && changedTypes.Count == 0 && delta != 0)
        {
            sb.Append(" No per-type breakdown differences detected despite the total moving - update the " +
                "pinned total AND re-run the snapshot generation, this file's data is internally " +
                "inconsistent.");
        }

        return sb.ToString();
    }

    private static void AppendTypeList(StringBuilder sb, string label, IReadOnlyList<string> types)
    {
        if (types.Count == 0)
        {
            return;
        }

        sb.Append("\n  ").Append(label).Append(" (").Append(types.Count).Append("): ")
            .Append(string.Join(", ", types.Take(20)));
        if (types.Count > 20)
        {
            sb.Append(", ...");
        }
    }

    private static SortedDictionary<string, string[]> ParseSnapshot(string block)
    {
        var result = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
            var typeName = line[..separatorIndex];
            var keys = line[(separatorIndex + 1)..].Split(',');
            result[typeName] = keys;
        }

        return result;
    }

    // Pinned snapshots below were captured by direct reflection over each shipped generated provider's
    // "_types" dictionary (the same source MeasureRootDeclaredConstraints reads), sorted by type name,
    // with each type's constraint keys sorted ordinally. Regenerate by running this test's measurement
    // logic against the provider directly and re-running the mismatch path once to get fresh output.

    private const string R4Snapshot = """
        Account=dom-2,dom-3,dom-4,dom-5,dom-6
        ActivityDefinition=adf-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Address=ele-1
        AdverseEvent=dom-2,dom-3,dom-4,dom-5,dom-6
        Age=age-1,ele-1,qty-3
        AllergyIntolerance=ait-1,ait-2,dom-2,dom-3,dom-4,dom-5,dom-6
        Annotation=ele-1
        Appointment=app-2,app-3,app-4,dom-2,dom-3,dom-4,dom-5,dom-6
        AppointmentResponse=apr-1,dom-2,dom-3,dom-4,dom-5,dom-6
        Attachment=att-1,ele-1
        AuditEvent=dom-2,dom-3,dom-4,dom-5,dom-6
        BackboneElement=ele-1
        Basic=dom-2,dom-3,dom-4,dom-5,dom-6
        BiologicallyDerivedProduct=dom-2,dom-3,dom-4,dom-5,dom-6
        BodyStructure=dom-2,dom-3,dom-4,dom-5,dom-6
        Bundle=bdl-1,bdl-10,bdl-11,bdl-12,bdl-2,bdl-3,bdl-4,bdl-7,bdl-9
        CapabilityStatement=cpb-0,cpb-1,cpb-14,cpb-15,cpb-16,cpb-2,cpb-3,cpb-7,dom-2,dom-3,dom-4,dom-5,dom-6
        CarePlan=dom-2,dom-3,dom-4,dom-5,dom-6
        CareTeam=dom-2,dom-3,dom-4,dom-5,dom-6
        CatalogEntry=dom-2,dom-3,dom-4,dom-5,dom-6
        ChargeItem=dom-2,dom-3,dom-4,dom-5,dom-6
        ChargeItemDefinition=cid-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Claim=dom-2,dom-3,dom-4,dom-5,dom-6
        ClaimResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        ClinicalImpression=dom-2,dom-3,dom-4,dom-5,dom-6
        CodeSystem=csd-0,csd-1,dom-2,dom-3,dom-4,dom-5,dom-6
        CodeableConcept=ele-1
        Coding=ele-1
        Communication=dom-2,dom-3,dom-4,dom-5,dom-6
        CommunicationRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        CompartmentDefinition=cpd-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Composition=dom-2,dom-3,dom-4,dom-5,dom-6
        ConceptMap=cmd-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Condition=con-3,con-4,con-5,dom-2,dom-3,dom-4,dom-5,dom-6
        Consent=dom-2,dom-3,dom-4,dom-5,dom-6,ppc-1,ppc-2,ppc-3,ppc-4,ppc-5
        ContactDetail=ele-1
        ContactPoint=cpt-2,ele-1
        Contract=dom-2,dom-3,dom-4,dom-5,dom-6
        Contributor=ele-1
        Count=cnt-3,ele-1,qty-3
        Coverage=dom-2,dom-3,dom-4,dom-5,dom-6
        CoverageEligibilityRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        CoverageEligibilityResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        DataRequirement=ele-1
        DetectedIssue=dom-2,dom-3,dom-4,dom-5,dom-6
        Device=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceMetric=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceUseStatement=dom-2,dom-3,dom-4,dom-5,dom-6
        DiagnosticReport=dom-2,dom-3,dom-4,dom-5,dom-6
        Distance=dis-1,ele-1,qty-3
        DocumentManifest=dom-2,dom-3,dom-4,dom-5,dom-6
        DocumentReference=dom-2,dom-3,dom-4,dom-5,dom-6
        DomainResource=dom-2,dom-3,dom-4,dom-5,dom-6
        Dosage=ele-1
        Duration=drt-1,ele-1,qty-3
        EffectEvidenceSynthesis=dom-2,dom-3,dom-4,dom-5,dom-6,ees-0
        Element=ele-1
        ElementDefinition=eld-11,eld-13,eld-14,eld-15,eld-16,eld-18,eld-19,eld-2,eld-20,eld-22,eld-5,eld-6,eld-7,eld-8,ele-1
        Encounter=dom-2,dom-3,dom-4,dom-5,dom-6
        Endpoint=dom-2,dom-3,dom-4,dom-5,dom-6
        EnrollmentRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        EnrollmentResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        EpisodeOfCare=dom-2,dom-3,dom-4,dom-5,dom-6
        EventDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,evd-0
        Evidence=dom-2,dom-3,dom-4,dom-5,dom-6,evi-0
        EvidenceVariable=dom-2,dom-3,dom-4,dom-5,dom-6,evv-0
        ExampleScenario=dom-2,dom-3,dom-4,dom-5,dom-6,esc-0
        ExplanationOfBenefit=dom-2,dom-3,dom-4,dom-5,dom-6
        Expression=ele-1,exp-1
        Extension=ele-1,ext-1
        FamilyMemberHistory=dom-2,dom-3,dom-4,dom-5,dom-6,fhs-1,fhs-2
        Flag=dom-2,dom-3,dom-4,dom-5,dom-6
        Goal=dom-2,dom-3,dom-4,dom-5,dom-6
        GraphDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,gdf-0
        Group=dom-2,dom-3,dom-4,dom-5,dom-6,grp-1
        GuidanceResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        HealthcareService=dom-2,dom-3,dom-4,dom-5,dom-6
        HumanName=ele-1
        Identifier=ele-1
        ImagingStudy=dom-2,dom-3,dom-4,dom-5,dom-6
        Immunization=dom-2,dom-3,dom-4,dom-5,dom-6
        ImmunizationEvaluation=dom-2,dom-3,dom-4,dom-5,dom-6
        ImmunizationRecommendation=dom-2,dom-3,dom-4,dom-5,dom-6
        ImplementationGuide=dom-2,dom-3,dom-4,dom-5,dom-6,ig-0,ig-2
        InsurancePlan=dom-2,dom-3,dom-4,dom-5,dom-6,ipn-1
        Invoice=dom-2,dom-3,dom-4,dom-5,dom-6
        Library=dom-2,dom-3,dom-4,dom-5,dom-6,lib-0
        Linkage=dom-2,dom-3,dom-4,dom-5,dom-6,lnk-1
        List=dom-2,dom-3,dom-4,dom-5,dom-6,lst-1,lst-2,lst-3
        Location=dom-2,dom-3,dom-4,dom-5,dom-6
        MarketingStatus=ele-1
        Measure=dom-2,dom-3,dom-4,dom-5,dom-6,mea-0,mea-1
        MeasureReport=dom-2,dom-3,dom-4,dom-5,dom-6,mrp-1,mrp-2
        Media=dom-2,dom-3,dom-4,dom-5,dom-6
        Medication=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationAdministration=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationDispense=dom-2,dom-3,dom-4,dom-5,dom-6,mdd-1
        MedicationKnowledge=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationStatement=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProduct=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductAuthorization=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductContraindication=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductIndication=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductIngredient=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductInteraction=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductManufactured=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductPackaged=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductPharmaceutical=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductUndesirableEffect=dom-2,dom-3,dom-4,dom-5,dom-6
        MessageDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,msd-0
        MessageHeader=dom-2,dom-3,dom-4,dom-5,dom-6
        Meta=ele-1
        MolecularSequence=dom-2,dom-3,dom-4,dom-5,dom-6,msq-3
        Money=ele-1
        NamingSystem=dom-2,dom-3,dom-4,dom-5,dom-6,nsd-0,nsd-1,nsd-2
        Narrative=ele-1
        NutritionOrder=dom-2,dom-3,dom-4,dom-5,dom-6,nor-1
        Observation=dom-2,dom-3,dom-4,dom-5,dom-6,obs-6,obs-7
        ObservationDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        OperationDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,opd-0
        OperationOutcome=dom-2,dom-3,dom-4,dom-5,dom-6
        Organization=dom-2,dom-3,dom-4,dom-5,dom-6,org-1
        OrganizationAffiliation=dom-2,dom-3,dom-4,dom-5,dom-6
        ParameterDefinition=ele-1
        Patient=dom-2,dom-3,dom-4,dom-5,dom-6
        PaymentNotice=dom-2,dom-3,dom-4,dom-5,dom-6
        PaymentReconciliation=dom-2,dom-3,dom-4,dom-5,dom-6
        Period=ele-1,per-1
        Person=dom-2,dom-3,dom-4,dom-5,dom-6
        PlanDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,pdf-0
        Population=ele-1
        Practitioner=dom-2,dom-3,dom-4,dom-5,dom-6
        PractitionerRole=dom-2,dom-3,dom-4,dom-5,dom-6
        Procedure=dom-2,dom-3,dom-4,dom-5,dom-6
        ProdCharacteristic=ele-1
        ProductShelfLife=ele-1
        Provenance=dom-2,dom-3,dom-4,dom-5,dom-6
        Quantity=ele-1,qty-3
        Questionnaire=dom-2,dom-3,dom-4,dom-5,dom-6,que-0,que-2
        QuestionnaireResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        Range=ele-1,rng-2
        Ratio=ele-1,rat-1
        Reference=ele-1,ref-1
        RelatedArtifact=ele-1
        RelatedPerson=dom-2,dom-3,dom-4,dom-5,dom-6
        RequestGroup=dom-2,dom-3,dom-4,dom-5,dom-6
        ResearchDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,rsd-0
        ResearchElementDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,red-0
        ResearchStudy=dom-2,dom-3,dom-4,dom-5,dom-6
        ResearchSubject=dom-2,dom-3,dom-4,dom-5,dom-6
        RiskAssessment=dom-2,dom-3,dom-4,dom-5,dom-6
        RiskEvidenceSynthesis=dom-2,dom-3,dom-4,dom-5,dom-6,rvs-0
        SampledData=ele-1
        Schedule=dom-2,dom-3,dom-4,dom-5,dom-6
        SearchParameter=dom-2,dom-3,dom-4,dom-5,dom-6,spd-0,spd-1,spd-2
        ServiceRequest=dom-2,dom-3,dom-4,dom-5,dom-6,prr-1
        Signature=ele-1
        Slot=dom-2,dom-3,dom-4,dom-5,dom-6
        Specimen=dom-2,dom-3,dom-4,dom-5,dom-6
        SpecimenDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        StructureDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,sdf-0,sdf-1,sdf-11,sdf-14,sdf-15,sdf-15a,sdf-16,sdf-17,sdf-18,sdf-19,sdf-21,sdf-22,sdf-23,sdf-4,sdf-5,sdf-6,sdf-9
        StructureMap=dom-2,dom-3,dom-4,dom-5,dom-6,smp-0
        Subscription=dom-2,dom-3,dom-4,dom-5,dom-6
        Substance=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceAmount=ele-1
        SubstanceNucleicAcid=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstancePolymer=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceProtein=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceReferenceInformation=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceSourceMaterial=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceSpecification=dom-2,dom-3,dom-4,dom-5,dom-6
        SupplyDelivery=dom-2,dom-3,dom-4,dom-5,dom-6
        SupplyRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        Task=dom-2,dom-3,dom-4,dom-5,dom-6,inv-1
        TerminologyCapabilities=dom-2,dom-3,dom-4,dom-5,dom-6,tcp-0,tcp-2,tcp-3,tcp-4,tcp-5
        TestReport=dom-2,dom-3,dom-4,dom-5,dom-6
        TestScript=dom-2,dom-3,dom-4,dom-5,dom-6,tst-0
        Timing=ele-1
        TriggerDefinition=ele-1,trd-1,trd-2,trd-3
        UsageContext=ele-1
        ValueSet=dom-2,dom-3,dom-4,dom-5,dom-6,vsd-0
        VerificationResult=dom-2,dom-3,dom-4,dom-5,dom-6
        VisionPrescription=dom-2,dom-3,dom-4,dom-5,dom-6
        base64Binary=ele-1
        boolean=ele-1
        canonical=ele-1
        code=ele-1
        date=ele-1
        dateTime=ele-1
        decimal=ele-1
        id=ele-1
        instant=ele-1
        integer=ele-1
        markdown=ele-1
        oid=ele-1
        positiveInt=ele-1
        string=ele-1
        time=ele-1
        unsignedInt=ele-1
        uri=ele-1
        url=ele-1
        uuid=ele-1
        xhtml=ele-1
        """;

    private const string R4BSnapshot = """
        Account=dom-2,dom-3,dom-4,dom-5,dom-6
        ActivityDefinition=cnl-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Address=ele-1
        AdministrableProductDefinition=apd-1,dom-2,dom-3,dom-4,dom-5,dom-6
        AdverseEvent=dom-2,dom-3,dom-4,dom-5,dom-6
        Age=age-1,ele-1,qty-3
        AllergyIntolerance=ait-1,ait-2,dom-2,dom-3,dom-4,dom-5,dom-6
        Annotation=ele-1
        Appointment=app-2,app-3,app-4,dom-2,dom-3,dom-4,dom-5,dom-6
        AppointmentResponse=apr-1,dom-2,dom-3,dom-4,dom-5,dom-6
        Attachment=att-1,ele-1
        AuditEvent=dom-2,dom-3,dom-4,dom-5,dom-6
        BackboneElement=ele-1
        Basic=dom-2,dom-3,dom-4,dom-5,dom-6
        BiologicallyDerivedProduct=dom-2,dom-3,dom-4,dom-5,dom-6
        BodyStructure=dom-2,dom-3,dom-4,dom-5,dom-6
        Bundle=bdl-1,bdl-10,bdl-11,bdl-12,bdl-2,bdl-3,bdl-4,bdl-7,bdl-9
        CapabilityStatement=cpb-0,cpb-1,cpb-14,cpb-15,cpb-16,cpb-2,cpb-3,cpb-7,dom-2,dom-3,dom-4,dom-5,dom-6
        CarePlan=dom-2,dom-3,dom-4,dom-5,dom-6
        CareTeam=dom-2,dom-3,dom-4,dom-5,dom-6
        CatalogEntry=dom-2,dom-3,dom-4,dom-5,dom-6
        ChargeItem=dom-2,dom-3,dom-4,dom-5,dom-6
        ChargeItemDefinition=cid-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Citation=cnl-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Claim=dom-2,dom-3,dom-4,dom-5,dom-6
        ClaimResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        ClinicalImpression=dom-2,dom-3,dom-4,dom-5,dom-6
        ClinicalUseDefinition=cud-1,dom-2,dom-3,dom-4,dom-5,dom-6
        CodeSystem=csd-0,csd-1,dom-2,dom-3,dom-4,dom-5,dom-6
        CodeableConcept=ele-1
        CodeableReference=ele-1
        Coding=ele-1
        Communication=dom-2,dom-3,dom-4,dom-5,dom-6
        CommunicationRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        CompartmentDefinition=cpd-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Composition=dom-2,dom-3,dom-4,dom-5,dom-6
        ConceptMap=cmd-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Condition=con-3,con-4,con-5,dom-2,dom-3,dom-4,dom-5,dom-6
        Consent=dom-2,dom-3,dom-4,dom-5,dom-6,ppc-1,ppc-2,ppc-3,ppc-4,ppc-5
        ContactDetail=ele-1
        ContactPoint=cpt-2,ele-1
        Contract=dom-2,dom-3,dom-4,dom-5,dom-6
        Contributor=ele-1
        Count=cnt-3,ele-1,qty-3
        Coverage=dom-2,dom-3,dom-4,dom-5,dom-6
        CoverageEligibilityRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        CoverageEligibilityResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        DataRequirement=ele-1
        DataType=ele-1
        DetectedIssue=dom-2,dom-3,dom-4,dom-5,dom-6
        Device=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceMetric=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceUseStatement=dom-2,dom-3,dom-4,dom-5,dom-6
        DiagnosticReport=dom-2,dom-3,dom-4,dom-5,dom-6
        Distance=dis-1,ele-1,qty-3
        DocumentManifest=dom-2,dom-3,dom-4,dom-5,dom-6
        DocumentReference=dom-2,dom-3,dom-4,dom-5,dom-6
        DomainResource=dom-2,dom-3,dom-4,dom-5,dom-6
        Dosage=ele-1
        Duration=drt-1,ele-1,qty-3
        Element=ele-1
        ElementDefinition=eld-11,eld-13,eld-14,eld-15,eld-16,eld-18,eld-19,eld-2,eld-20,eld-22,eld-5,eld-6,eld-7,eld-8,ele-1
        Encounter=dom-2,dom-3,dom-4,dom-5,dom-6
        Endpoint=dom-2,dom-3,dom-4,dom-5,dom-6
        EnrollmentRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        EnrollmentResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        EpisodeOfCare=dom-2,dom-3,dom-4,dom-5,dom-6
        EventDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,evd-0
        Evidence=cnl-0,dom-2,dom-3,dom-4,dom-5,dom-6
        EvidenceReport=cnl-0,dom-2,dom-3,dom-4,dom-5,dom-6
        EvidenceVariable=cnl-0,dom-2,dom-3,dom-4,dom-5,dom-6
        ExampleScenario=dom-2,dom-3,dom-4,dom-5,dom-6,esc-0
        ExplanationOfBenefit=dom-2,dom-3,dom-4,dom-5,dom-6
        Expression=ele-1,exp-1
        Extension=ele-1,ext-1
        FamilyMemberHistory=dom-2,dom-3,dom-4,dom-5,dom-6,fhs-1,fhs-2
        Flag=dom-2,dom-3,dom-4,dom-5,dom-6
        Goal=dom-2,dom-3,dom-4,dom-5,dom-6
        GraphDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,gdf-0
        Group=dom-2,dom-3,dom-4,dom-5,dom-6,grp-1
        GuidanceResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        HealthcareService=dom-2,dom-3,dom-4,dom-5,dom-6
        HumanName=ele-1
        Identifier=ele-1
        ImagingStudy=dom-2,dom-3,dom-4,dom-5,dom-6
        Immunization=dom-2,dom-3,dom-4,dom-5,dom-6
        ImmunizationEvaluation=dom-2,dom-3,dom-4,dom-5,dom-6
        ImmunizationRecommendation=dom-2,dom-3,dom-4,dom-5,dom-6
        ImplementationGuide=dom-2,dom-3,dom-4,dom-5,dom-6,ig-0,ig-2
        Ingredient=dom-2,dom-3,dom-4,dom-5,dom-6,ing-1
        InsurancePlan=dom-2,dom-3,dom-4,dom-5,dom-6,ipn-1
        Invoice=dom-2,dom-3,dom-4,dom-5,dom-6
        Library=cnl-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Linkage=dom-2,dom-3,dom-4,dom-5,dom-6,lnk-1
        List=dom-2,dom-3,dom-4,dom-5,dom-6,lst-1,lst-2,lst-3
        Location=dom-2,dom-3,dom-4,dom-5,dom-6
        ManufacturedItemDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        MarketingStatus=ele-1
        Measure=cnl-0,dom-2,dom-3,dom-4,dom-5,dom-6,mea-1
        MeasureReport=dom-2,dom-3,dom-4,dom-5,dom-6,mrp-1,mrp-2
        Media=dom-2,dom-3,dom-4,dom-5,dom-6
        Medication=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationAdministration=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationDispense=dom-2,dom-3,dom-4,dom-5,dom-6,mdd-1
        MedicationKnowledge=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationStatement=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        MessageDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,msd-0
        MessageHeader=dom-2,dom-3,dom-4,dom-5,dom-6
        Meta=ele-1
        MolecularSequence=dom-2,dom-3,dom-4,dom-5,dom-6,msq-3
        Money=ele-1
        NamingSystem=dom-2,dom-3,dom-4,dom-5,dom-6,nsd-0,nsd-1,nsd-2
        Narrative=ele-1
        NutritionOrder=dom-2,dom-3,dom-4,dom-5,dom-6,nor-1
        NutritionProduct=dom-2,dom-3,dom-4,dom-5,dom-6
        Observation=dom-2,dom-3,dom-4,dom-5,dom-6,obs-6,obs-7
        ObservationDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        OperationDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,opd-0
        OperationOutcome=dom-2,dom-3,dom-4,dom-5,dom-6
        Organization=dom-2,dom-3,dom-4,dom-5,dom-6,org-1
        OrganizationAffiliation=dom-2,dom-3,dom-4,dom-5,dom-6
        PackagedProductDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        ParameterDefinition=ele-1
        Patient=dom-2,dom-3,dom-4,dom-5,dom-6
        PaymentNotice=dom-2,dom-3,dom-4,dom-5,dom-6
        PaymentReconciliation=dom-2,dom-3,dom-4,dom-5,dom-6
        Period=ele-1,per-1
        Person=dom-2,dom-3,dom-4,dom-5,dom-6
        PlanDefinition=cnl-0,dom-2,dom-3,dom-4,dom-5,dom-6
        Population=ele-1
        Practitioner=dom-2,dom-3,dom-4,dom-5,dom-6
        PractitionerRole=dom-2,dom-3,dom-4,dom-5,dom-6
        Procedure=dom-2,dom-3,dom-4,dom-5,dom-6
        ProdCharacteristic=ele-1
        ProductShelfLife=ele-1
        Provenance=dom-2,dom-3,dom-4,dom-5,dom-6
        Quantity=ele-1,qty-3
        Questionnaire=dom-2,dom-3,dom-4,dom-5,dom-6,que-0,que-2
        QuestionnaireResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        Range=ele-1,rng-2
        Ratio=ele-1,rat-1
        RatioRange=ele-1,inv-1,inv-2
        Reference=ele-1,ref-1
        RegulatedAuthorization=dom-2,dom-3,dom-4,dom-5,dom-6
        RelatedArtifact=ele-1
        RelatedPerson=dom-2,dom-3,dom-4,dom-5,dom-6
        RequestGroup=dom-2,dom-3,dom-4,dom-5,dom-6
        ResearchDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,rsd-0
        ResearchElementDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,red-0
        ResearchStudy=dom-2,dom-3,dom-4,dom-5,dom-6
        ResearchSubject=dom-2,dom-3,dom-4,dom-5,dom-6
        RiskAssessment=dom-2,dom-3,dom-4,dom-5,dom-6
        SampledData=ele-1
        Schedule=dom-2,dom-3,dom-4,dom-5,dom-6
        SearchParameter=dom-2,dom-3,dom-4,dom-5,dom-6,spd-0,spd-1,spd-2
        ServiceRequest=dom-2,dom-3,dom-4,dom-5,dom-6,prr-1
        Signature=ele-1
        Slot=dom-2,dom-3,dom-4,dom-5,dom-6
        Specimen=dom-2,dom-3,dom-4,dom-5,dom-6
        SpecimenDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        StructureDefinition=dom-2,dom-3,dom-4,dom-5,dom-6,sdf-0,sdf-1,sdf-11,sdf-14,sdf-15,sdf-15a,sdf-16,sdf-17,sdf-18,sdf-19,sdf-21,sdf-22,sdf-23,sdf-4,sdf-5,sdf-6,sdf-9
        StructureMap=dom-2,dom-3,dom-4,dom-5,dom-6,smp-0
        Subscription=dom-2,dom-3,dom-4,dom-5,dom-6
        SubscriptionStatus=dom-2,dom-3,dom-4,dom-5,dom-6,sst-1
        SubscriptionTopic=dom-2,dom-3,dom-4,dom-5,dom-6
        Substance=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        SupplyDelivery=dom-2,dom-3,dom-4,dom-5,dom-6
        SupplyRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        Task=dom-2,dom-3,dom-4,dom-5,dom-6,inv-1
        TerminologyCapabilities=dom-2,dom-3,dom-4,dom-5,dom-6,tcp-0,tcp-2,tcp-3,tcp-4,tcp-5
        TestReport=dom-2,dom-3,dom-4,dom-5,dom-6
        TestScript=dom-2,dom-3,dom-4,dom-5,dom-6,tst-0
        Timing=ele-1
        TriggerDefinition=ele-1,trd-1,trd-2,trd-3
        UsageContext=ele-1
        ValueSet=dom-2,dom-3,dom-4,dom-5,dom-6,vsd-0
        VerificationResult=dom-2,dom-3,dom-4,dom-5,dom-6
        VisionPrescription=dom-2,dom-3,dom-4,dom-5,dom-6
        base64Binary=ele-1
        boolean=ele-1
        canonical=ele-1
        code=ele-1
        date=ele-1
        dateTime=ele-1
        decimal=ele-1
        id=ele-1
        instant=ele-1
        integer=ele-1
        markdown=ele-1
        oid=ele-1
        positiveInt=ele-1
        string=ele-1
        time=ele-1
        unsignedInt=ele-1
        uri=ele-1
        url=ele-1
        uuid=ele-1
        xhtml=ele-1
        """;

    private const string R5Snapshot = """
        Account=dom-2,dom-3,dom-4,dom-5,dom-6
        ActivityDefinition=cnl-0
        ActorDefinition=cnl-0
        Address=ele-1
        AdministrableProductDefinition=apd-1,dom-2,dom-3,dom-4,dom-5,dom-6
        AdverseEvent=dom-2,dom-3,dom-4,dom-5,dom-6
        Age=age-1,ele-1,qty-3
        AllergyIntolerance=dom-2,dom-3,dom-4,dom-5,dom-6
        Annotation=ele-1
        Appointment=app-2,app-3,app-4,app-5,app-6,app-7,dom-2,dom-3,dom-4,dom-5,dom-6
        AppointmentResponse=apr-1,dom-2,dom-3,dom-4,dom-5,dom-6
        ArtifactAssessment=dom-2,dom-3,dom-4,dom-5,dom-6
        Attachment=att-1,ele-1
        AuditEvent=dom-2,dom-3,dom-4,dom-5,dom-6
        Availability=ele-1
        BackboneElement=ele-1
        BackboneType=ele-1
        Base=ele-1
        Basic=dom-2,dom-3,dom-4,dom-5,dom-6
        BiologicallyDerivedProduct=dom-2,dom-3,dom-4,dom-5,dom-6
        BiologicallyDerivedProductDispense=dom-2,dom-3,dom-4,dom-5,dom-6
        BodyStructure=dom-2,dom-3,dom-4,dom-5,dom-6
        Bundle=bdl-1,bdl-10,bdl-11,bdl-12,bdl-13,bdl-14,bdl-15,bdl-16,bdl-17,bdl-18,bdl-2,bdl-3a,bdl-3b,bdl-3c,bdl-3d,bdl-7,bdl-9
        CapabilityStatement=cnl-0,cpb-1,cpb-14,cpb-15,cpb-16,cpb-2,cpb-3,cpb-4,cpb-7
        CarePlan=dom-2,dom-3,dom-4,dom-5,dom-6
        CareTeam=dom-2,dom-3,dom-4,dom-5,dom-6
        ChargeItem=dom-2,dom-3,dom-4,dom-5,dom-6
        ChargeItemDefinition=cnl-0
        Citation=cnl-0
        Claim=dom-2,dom-3,dom-4,dom-5,dom-6
        ClaimResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        ClinicalImpression=dom-2,dom-3,dom-4,dom-5,dom-6
        ClinicalUseDefinition=cud-1,dom-2,dom-3,dom-4,dom-5,dom-6
        CodeSystem=cnl-0,csd-1,csd-2,csd-3,csd-4
        CodeableConcept=ele-1
        CodeableReference=ele-1
        Coding=cod-1,ele-1
        Communication=dom-2,dom-3,dom-4,dom-5,dom-6
        CommunicationRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        CompartmentDefinition=cnl-0
        Composition=dom-2,dom-3,dom-4,dom-5,dom-6
        ConceptMap=cnl-0
        Condition=con-2,con-3,dom-2,dom-3,dom-4,dom-5,dom-6
        ConditionDefinition=cnl-0
        Consent=dom-2,dom-3,dom-4,dom-5,dom-6
        ContactDetail=ele-1
        ContactPoint=cpt-2,ele-1
        Contract=dom-2,dom-3,dom-4,dom-5,dom-6
        Contributor=ele-1
        Count=cnt-3,ele-1,qty-3
        Coverage=dom-2,dom-3,dom-4,dom-5,dom-6
        CoverageEligibilityRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        CoverageEligibilityResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        DataRequirement=ele-1
        DataType=ele-1
        DetectedIssue=dom-2,dom-3,dom-4,dom-5,dom-6
        Device=dev-1,dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceAssociation=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceDispense=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceMetric=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceUsage=dom-2,dom-3,dom-4,dom-5,dom-6
        DiagnosticReport=dgr-1,dom-2,dom-3,dom-4,dom-5,dom-6
        Distance=dis-1,ele-1,qty-3
        DocumentReference=docRef-1,docRef-2,dom-2,dom-3,dom-4,dom-5,dom-6
        DomainResource=dom-2,dom-3,dom-4,dom-5,dom-6
        Dosage=dos-1,ele-1
        Duration=drt-1,ele-1,qty-3
        Element=ele-1
        ElementDefinition=eld-11,eld-13,eld-14,eld-15,eld-16,eld-18,eld-19,eld-2,eld-20,eld-22,eld-24,eld-25,eld-27,eld-28,eld-5,eld-6,eld-7,eld-8,ele-1
        Encounter=dom-2,dom-3,dom-4,dom-5,dom-6
        EncounterHistory=dom-2,dom-3,dom-4,dom-5,dom-6
        Endpoint=dom-2,dom-3,dom-4,dom-5,dom-6
        EnrollmentRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        EnrollmentResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        EpisodeOfCare=dom-2,dom-3,dom-4,dom-5,dom-6
        EventDefinition=cnl-0
        Evidence=cnl-0
        EvidenceVariable=cnl-0
        ExampleScenario=cnl-0,exs-12,exs-3,exs-4,exs-6,exs-7,exs-8,exs-9
        ExplanationOfBenefit=dom-2,dom-3,dom-4,dom-5,dom-6
        Expression=ele-1,exp-1,exp-2
        ExtendedContactDetail=ele-1
        Extension=ele-1,ext-1
        FamilyMemberHistory=dom-2,dom-3,dom-4,dom-5,dom-6,fhs-1,fhs-2,fhs-3
        Flag=dom-2,dom-3,dom-4,dom-5,dom-6
        FormularyItem=dom-2,dom-3,dom-4,dom-5,dom-6
        GenomicStudy=dom-2,dom-3,dom-4,dom-5,dom-6
        Goal=dom-2,dom-3,dom-4,dom-5,dom-6
        GraphDefinition=cnl-0
        Group=dom-2,dom-3,dom-4,dom-5,dom-6
        GuidanceResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        HealthcareService=dom-2,dom-3,dom-4,dom-5,dom-6
        HumanName=ele-1
        Identifier=ele-1,ident-1
        ImagingSelection=dom-2,dom-3,dom-4,dom-5,dom-6
        ImagingStudy=dom-2,dom-3,dom-4,dom-5,dom-6
        Immunization=dom-2,dom-3,dom-4,dom-5,dom-6
        ImmunizationEvaluation=dom-2,dom-3,dom-4,dom-5,dom-6
        ImmunizationRecommendation=dom-2,dom-3,dom-4,dom-5,dom-6
        ImplementationGuide=cnl-0,ig-2
        Ingredient=dom-2,dom-3,dom-4,dom-5,dom-6,ing-1
        InsurancePlan=dom-2,dom-3,dom-4,dom-5,dom-6,ipn-1
        InventoryItem=dom-2,dom-3,dom-4,dom-5,dom-6
        InventoryReport=dom-2,dom-3,dom-4,dom-5,dom-6
        Invoice=dom-2,dom-3,dom-4,dom-5,dom-6
        Library=cnl-0
        Linkage=dom-2,dom-3,dom-4,dom-5,dom-6,lnk-1
        List=dom-2,dom-3,dom-4,dom-5,dom-6,lst-1
        Location=dom-2,dom-3,dom-4,dom-5,dom-6
        ManufacturedItemDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        MarketingStatus=ele-1
        Measure=cnl-0,mea-1
        MeasureReport=dom-2,dom-3,dom-4,dom-5,dom-6,mrp-1,mrp-2
        Medication=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationAdministration=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationDispense=dom-2,dom-3,dom-4,dom-5,dom-6,mdd-1
        MedicationRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationStatement=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        MessageDefinition=cnl-0
        MessageHeader=dom-2,dom-3,dom-4,dom-5,dom-6
        Meta=ele-1
        MolecularSequence=dom-2,dom-3,dom-4,dom-5,dom-6
        MonetaryComponent=ele-1
        Money=ele-1
        NamingSystem=cnl-0,nsd-1,nsd-2,nsd-3
        Narrative=ele-1
        NutritionIntake=dom-2,dom-3,dom-4,dom-5,dom-6
        NutritionOrder=dom-2,dom-3,dom-4,dom-5,dom-6,nor-1
        NutritionProduct=dom-2,dom-3,dom-4,dom-5,dom-6
        Observation=dom-2,dom-3,dom-4,dom-5,dom-6,obs-6,obs-7,obs-8
        ObservationDefinition=cnl-0,obd-0
        OperationDefinition=cnl-0,opd-5,opd-6,opd-7
        OperationOutcome=dom-2,dom-3,dom-4,dom-5,dom-6
        Organization=dom-2,dom-3,dom-4,dom-5,dom-6,org-1
        OrganizationAffiliation=dom-2,dom-3,dom-4,dom-5,dom-6
        PackagedProductDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        ParameterDefinition=ele-1
        Patient=dom-2,dom-3,dom-4,dom-5,dom-6
        PaymentNotice=dom-2,dom-3,dom-4,dom-5,dom-6
        PaymentReconciliation=dom-2,dom-3,dom-4,dom-5,dom-6
        Period=ele-1,per-1
        Permission=dom-2,dom-3,dom-4,dom-5,dom-6
        Person=dom-2,dom-3,dom-4,dom-5,dom-6
        PlanDefinition=cnl-0,pld-3,pld-4
        Practitioner=dom-2,dom-3,dom-4,dom-5,dom-6
        PractitionerRole=dom-2,dom-3,dom-4,dom-5,dom-6
        PrimitiveType=ele-1
        Procedure=dom-2,dom-3,dom-4,dom-5,dom-6
        ProductShelfLife=ele-1
        Provenance=dom-2,dom-3,dom-4,dom-5,dom-6
        Quantity=ele-1,qty-3
        Questionnaire=cnl-0,que-2
        QuestionnaireResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        Range=ele-1,rng-2
        Ratio=ele-1,rat-1
        RatioRange=ele-1,ratrng-1,ratrng-2
        Reference=ele-1,ref-1,ref-2
        RegulatedAuthorization=dom-2,dom-3,dom-4,dom-5,dom-6
        RelatedArtifact=ele-1
        RelatedPerson=dom-2,dom-3,dom-4,dom-5,dom-6
        RequestOrchestration=dom-2,dom-3,dom-4,dom-5,dom-6
        Requirements=cnl-0
        ResearchStudy=dom-2,dom-3,dom-4,dom-5,dom-6
        ResearchSubject=dom-2,dom-3,dom-4,dom-5,dom-6
        RiskAssessment=dom-2,dom-3,dom-4,dom-5,dom-6
        SampledData=ele-1,sdd-1
        Schedule=dom-2,dom-3,dom-4,dom-5,dom-6
        SearchParameter=cnl-0,spd-1,spd-2,spd-3
        ServiceRequest=bdystr-1,dom-2,dom-3,dom-4,dom-5,dom-6,prr-1
        Signature=ele-1
        Slot=dom-2,dom-3,dom-4,dom-5,dom-6
        Specimen=dom-2,dom-3,dom-4,dom-5,dom-6
        StructureDefinition=cnl-0,sdf-1,sdf-11,sdf-14,sdf-15,sdf-15a,sdf-16,sdf-17,sdf-18,sdf-19,sdf-21,sdf-22,sdf-23,sdf-27,sdf-29,sdf-4,sdf-5,sdf-6,sdf-9
        StructureMap=cnl-0
        Subscription=dom-2,dom-3,dom-4,dom-5,dom-6
        SubscriptionStatus=dom-2,dom-3,dom-4,dom-5,dom-6,sst-1,sst-2
        Substance=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceNucleicAcid=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstancePolymer=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceProtein=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceReferenceInformation=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceSourceMaterial=dom-2,dom-3,dom-4,dom-5,dom-6
        SupplyDelivery=dom-2,dom-3,dom-4,dom-5,dom-6
        SupplyRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        Task=dom-2,dom-3,dom-4,dom-5,dom-6,inv-1,tsk-1
        TerminologyCapabilities=cnl-0,tcp-2,tcp-3,tcp-4,tcp-5,tcp-6
        TestPlan=cnl-0
        TestReport=dom-2,dom-3,dom-4,dom-5,dom-6
        TestScript=cnl-0
        Timing=ele-1
        Transport=dom-2,dom-3,dom-4,dom-5,dom-6
        TriggerDefinition=ele-1,trd-1,trd-2,trd-3
        UsageContext=ele-1
        ValueSet=cnl-0
        VerificationResult=dom-2,dom-3,dom-4,dom-5,dom-6
        VirtualServiceDetail=ele-1
        VisionPrescription=dom-2,dom-3,dom-4,dom-5,dom-6
        base64Binary=ele-1
        boolean=ele-1
        canonical=ele-1
        code=ele-1
        date=ele-1
        dateTime=ele-1
        decimal=ele-1
        id=ele-1
        instant=ele-1
        integer=ele-1
        integer64=ele-1
        markdown=ele-1
        oid=ele-1
        positiveInt=ele-1
        string=ele-1
        time=ele-1
        unsignedInt=ele-1
        uri=ele-1
        url=ele-1
        uuid=ele-1
        xhtml=ele-1
        """;

    private const string R6Snapshot = """
        Account=dom-2,dom-3,dom-4,dom-5,dom-6
        ActivityDefinition=cnl-0
        ActorDefinition=cnl-0
        Address=ele-1
        AdministrableProductDefinition=apd-1,dom-2,dom-3,dom-4,dom-5,dom-6
        AdverseEvent=dom-2,dom-3,dom-4,dom-5,dom-6
        Age=age-1,ele-1,qty-3
        AllergyIntolerance=dom-2,dom-3,dom-4,dom-5,dom-6
        Annotation=ele-1
        Appointment=app-2,app-3,app-4,app-5,app-6,app-7,dom-2,dom-3,dom-4,dom-5,dom-6
        AppointmentResponse=apr-1,dom-2,dom-3,dom-4,dom-5,dom-6
        ArtifactAssessment=dom-2,dom-3,dom-4,dom-5,dom-6
        Attachment=att-1,ele-1
        AuditEvent=dom-2,dom-3,dom-4,dom-5,dom-6
        Availability=ele-1
        BackboneElement=ele-1
        BackboneType=ele-1
        Basic=dom-2,dom-3,dom-4,dom-5,dom-6
        BiologicallyDerivedProduct=dom-2,dom-3,dom-4,dom-5,dom-6
        BiologicallyDerivedProductDispense=dom-2,dom-3,dom-4,dom-5,dom-6
        BodyStructure=dom-2,dom-3,dom-4,dom-5,dom-6
        Bundle=bdl-1,bdl-10,bdl-11,bdl-12,bdl-13,bdl-14,bdl-15,bdl-16,bdl-17,bdl-18,bdl-2,bdl-3a,bdl-3b,bdl-3c,bdl-3d,bdl-7,bdl-9
        CapabilityStatement=cnl-0,cpb-1,cpb-14,cpb-15,cpb-16,cpb-2,cpb-3,cpb-4,cpb-7
        CarePlan=dom-2,dom-3,dom-4,dom-5,dom-6
        CareTeam=dom-2,dom-3,dom-4,dom-5,dom-6
        ChargeItem=dom-2,dom-3,dom-4,dom-5,dom-6
        ChargeItemDefinition=cnl-0
        Citation=cnl-0
        Claim=dom-2,dom-3,dom-4,dom-5,dom-6
        ClaimResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        ClinicalImpression=dom-2,dom-3,dom-4,dom-5,dom-6
        ClinicalUseDefinition=cud-1,dom-2,dom-3,dom-4,dom-5,dom-6
        CodeSystem=cnl-0,csd-1,csd-2,csd-3,csd-4
        CodeableConcept=ele-1
        CodeableReference=ele-1
        Coding=cod-1,ele-1
        Communication=dom-2,dom-3,dom-4,dom-5,dom-6
        CommunicationRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        CompartmentDefinition=cnl-0
        Composition=dom-2,dom-3,dom-4,dom-5,dom-6
        ConceptMap=cnl-0
        Condition=con-2,con-3,con-4,dom-2,dom-3,dom-4,dom-5,dom-6
        ConditionDefinition=cnl-0
        Consent=dom-2,dom-3,dom-4,dom-5,dom-6
        ContactDetail=ele-1
        ContactPoint=cpt-2,ele-1
        Contract=dom-2,dom-3,dom-4,dom-5,dom-6
        Contributor=ele-1
        Count=cnt-3,ele-1,qty-3
        Coverage=dom-2,dom-3,dom-4,dom-5,dom-6
        CoverageEligibilityRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        CoverageEligibilityResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        DataRequirement=ele-1
        DataType=ele-1
        DetectedIssue=dom-2,dom-3,dom-4,dom-5,dom-6
        Device=dev-1,dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceAlert=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceAssociation=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceDispense=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceMetric=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        DeviceUsage=dom-2,dom-3,dom-4,dom-5,dom-6
        DiagnosticReport=dgr-1,dom-2,dom-3,dom-4,dom-5,dom-6
        Distance=dis-1,ele-1,qty-3
        DocumentReference=docRef-1,docRef-2,dom-2,dom-3,dom-4,dom-5,dom-6
        DomainResource=dom-2,dom-3,dom-4,dom-5,dom-6
        Dosage=dos-1,ele-1
        Duration=drt-1,ele-1,qty-3
        Element=ele-1
        ElementDefinition=eld-11,eld-13,eld-14,eld-15,eld-16,eld-18,eld-19,eld-2,eld-20,eld-22,eld-24,eld-25,eld-27,eld-28,eld-5,eld-6,eld-7,eld-8,ele-1
        Encounter=dom-2,dom-3,dom-4,dom-5,dom-6
        EncounterHistory=dom-2,dom-3,dom-4,dom-5,dom-6
        Endpoint=dom-2,dom-3,dom-4,dom-5,dom-6
        EnrollmentRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        EnrollmentResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        EpisodeOfCare=dom-2,dom-3,dom-4,dom-5,dom-6
        EventDefinition=cnl-0
        Evidence=cnl-0
        EvidenceVariable=cnl-0
        ExampleScenario=cnl-0,exs-12,exs-3,exs-4,exs-6,exs-7,exs-8,exs-9
        ExplanationOfBenefit=dom-2,dom-3,dom-4,dom-5,dom-6
        Expression=ele-1,exp-1,exp-2
        ExtendedContactDetail=ele-1
        Extension=ele-1,ext-1
        FamilyMemberHistory=dom-2,dom-3,dom-4,dom-5,dom-6,fhs-1,fhs-2,fhs-3
        Flag=dom-2,dom-3,dom-4,dom-5,dom-6
        FormularyItem=dom-2,dom-3,dom-4,dom-5,dom-6
        GenomicStudy=dom-2,dom-3,dom-4,dom-5,dom-6
        Goal=dom-2,dom-3,dom-4,dom-5,dom-6
        GraphDefinition=cnl-0
        Group=cnl-2
        GuidanceResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        HealthcareService=dom-2,dom-3,dom-4,dom-5,dom-6
        HumanName=ele-1
        Identifier=ele-1,ident-1
        ImagingSelection=dom-2,dom-3,dom-4,dom-5,dom-6
        ImagingStudy=dom-2,dom-3,dom-4,dom-5,dom-6
        Immunization=dom-2,dom-3,dom-4,dom-5,dom-6
        ImmunizationEvaluation=dom-2,dom-3,dom-4,dom-5,dom-6
        ImmunizationRecommendation=dom-2,dom-3,dom-4,dom-5,dom-6
        ImplementationGuide=cnl-0,ig-2
        Ingredient=dom-2,dom-3,dom-4,dom-5,dom-6,ing-1
        InsurancePlan=dom-2,dom-3,dom-4,dom-5,dom-6
        InsuranceProduct=dom-2,dom-3,dom-4,dom-5,dom-6,ipn-1
        InventoryItem=dom-2,dom-3,dom-4,dom-5,dom-6
        InventoryReport=dom-2,dom-3,dom-4,dom-5,dom-6
        Invoice=dom-2,dom-3,dom-4,dom-5,dom-6
        Library=cnl-0
        Linkage=dom-2,dom-3,dom-4,dom-5,dom-6,lnk-1
        List=dom-2,dom-3,dom-4,dom-5,dom-6,lst-1
        Location=dom-2,dom-3,dom-4,dom-5,dom-6
        ManufacturedItemDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        MarketingStatus=ele-1
        Measure=cnl-0,mea-1
        MeasureReport=dom-2,dom-3,dom-4,dom-5,dom-6,mrp-1,mrp-2
        Medication=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationAdministration=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationDispense=dom-2,dom-3,dom-4,dom-5,dom-6,mdd-1
        MedicationRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicationStatement=dom-2,dom-3,dom-4,dom-5,dom-6
        MedicinalProductDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        MessageDefinition=cnl-0
        MessageHeader=dom-2,dom-3,dom-4,dom-5,dom-6
        Meta=ele-1
        MolecularDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        MolecularSequence=dom-2,dom-3,dom-4,dom-5,dom-6
        MonetaryComponent=ele-1
        Money=ele-1
        NamingSystem=cnl-0,nsd-1,nsd-2,nsd-3
        Narrative=ele-1
        NutritionIntake=dom-2,dom-3,dom-4,dom-5,dom-6
        NutritionOrder=dom-2,dom-3,dom-4,dom-5,dom-6,nor-1
        NutritionProduct=dom-2,dom-3,dom-4,dom-5,dom-6
        Observation=dom-2,dom-3,dom-4,dom-5,dom-6,obs-6,obs-7,obs-8
        ObservationDefinition=cnl-0,obd-0
        OperationDefinition=cnl-0,opd-5,opd-6,opd-7
        OperationOutcome=dom-2,dom-3,dom-4,dom-5,dom-6
        Organization=dom-2,dom-3,dom-4,dom-5,dom-6,org-1
        OrganizationAffiliation=dom-2,dom-3,dom-4,dom-5,dom-6
        PackagedProductDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        ParameterDefinition=ele-1
        Patient=dom-2,dom-3,dom-4,dom-5,dom-6
        PaymentNotice=dom-2,dom-3,dom-4,dom-5,dom-6
        PaymentReconciliation=dom-2,dom-3,dom-4,dom-5,dom-6
        Period=ele-1,per-1
        Permission=dom-2,dom-3,dom-4,dom-5,dom-6
        Person=dom-2,dom-3,dom-4,dom-5,dom-6
        PlanDefinition=cnl-0,pld-3,pld-4
        Practitioner=dom-2,dom-3,dom-4,dom-5,dom-6
        PractitionerRole=dom-2,dom-3,dom-4,dom-5,dom-6
        PrimitiveType=ele-1
        Procedure=con-4,dom-2,dom-3,dom-4,dom-5,dom-6
        ProductShelfLife=ele-1
        Provenance=dom-2,dom-3,dom-4,dom-5,dom-6
        Quantity=ele-1,qty-3
        Questionnaire=cnl-0,que-2
        QuestionnaireResponse=dom-2,dom-3,dom-4,dom-5,dom-6
        Range=ele-1,rng-2
        Ratio=ele-1,rat-1
        RatioRange=ele-1,ratrng-1,ratrng-2
        Reference=ele-1,ref-1,ref-2
        RegulatedAuthorization=dom-2,dom-3,dom-4,dom-5,dom-6
        RelatedArtifact=ele-1
        RelatedPerson=dom-2,dom-3,dom-4,dom-5,dom-6
        RelativeTime=ele-1,rlt-1,rlt-2
        RequestOrchestration=dom-2,dom-3,dom-4,dom-5,dom-6
        Requirements=cnl-0
        ResearchStudy=dom-2,dom-3,dom-4,dom-5,dom-6
        ResearchSubject=dom-2,dom-3,dom-4,dom-5,dom-6
        RiskAssessment=dom-2,dom-3,dom-4,dom-5,dom-6
        SampledData=ele-1,sdd-1
        Schedule=dom-2,dom-3,dom-4,dom-5,dom-6
        SearchParameter=cnl-0,spd-1,spd-2,spd-3
        ServiceRequest=bdystr-1,dom-2,dom-3,dom-4,dom-5,dom-6,prr-1
        Signature=ele-1
        Slot=dom-2,dom-3,dom-4,dom-5,dom-6
        Specimen=dom-2,dom-3,dom-4,dom-5,dom-6,spm-1
        StructureDefinition=cnl-0,sdf-1,sdf-11,sdf-14,sdf-15,sdf-15a,sdf-16,sdf-17,sdf-18,sdf-19,sdf-21,sdf-22,sdf-23,sdf-27,sdf-29,sdf-4,sdf-5,sdf-6,sdf-9
        StructureMap=cnl-0
        Subscription=dom-2,dom-3,dom-4,dom-5,dom-6
        SubscriptionStatus=dom-2,dom-3,dom-4,dom-5,dom-6,sst-1,sst-2
        Substance=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceDefinition=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceNucleicAcid=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstancePolymer=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceProtein=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceReferenceInformation=dom-2,dom-3,dom-4,dom-5,dom-6
        SubstanceSourceMaterial=dom-2,dom-3,dom-4,dom-5,dom-6
        SupplyDelivery=dom-2,dom-3,dom-4,dom-5,dom-6
        SupplyRequest=dom-2,dom-3,dom-4,dom-5,dom-6
        Task=dom-2,dom-3,dom-4,dom-5,dom-6,inv-1,tsk-1
        TerminologyCapabilities=cnl-0,tcp-2,tcp-3,tcp-4,tcp-5,tcp-6
        TestPlan=cnl-0
        TestReport=dom-2,dom-3,dom-4,dom-5,dom-6
        TestScript=cnl-0
        Timing=ele-1
        Transport=dom-2,dom-3,dom-4,dom-5,dom-6
        TriggerDefinition=ele-1,trd-1,trd-2,trd-3
        UsageContext=ele-1
        ValueSet=cnl-0
        VerificationResult=dom-2,dom-3,dom-4,dom-5,dom-6
        VirtualServiceDetail=ele-1
        VisionPrescription=dom-2,dom-3,dom-4,dom-5,dom-6
        base64Binary=ele-1
        boolean=ele-1
        canonical=ele-1
        code=ele-1
        date=ele-1
        dateTime=ele-1
        decimal=ele-1
        id=ele-1
        instant=ele-1
        integer=ele-1
        integer64=ele-1
        markdown=ele-1
        oid=ele-1
        positiveInt=ele-1
        string=ele-1
        time=ele-1
        unsignedInt=ele-1
        uri=ele-1
        url=ele-1
        uuid=ele-1
        xhtml=ele-1
        """;

    private const string STU3Snapshot = """
        Account=dom-1,dom-2,dom-3,dom-4
        ActivityDefinition=dom-1,dom-2,dom-3,dom-4
        Address=ele-1
        AdverseEvent=dom-1,dom-2,dom-3,dom-4
        Age=age-1,qty-3
        AllergyIntolerance=ait-1,ait-2,dom-1,dom-2,dom-3,dom-4
        Annotation=ele-1
        Appointment=app-2,app-3,dom-1,dom-2,dom-3,dom-4
        AppointmentResponse=apr-1,dom-1,dom-2,dom-3,dom-4
        Attachment=att-1,ele-1
        AuditEvent=dom-1,dom-2,dom-3,dom-4
        BackboneElement=ele-1
        Basic=dom-1,dom-2,dom-3,dom-4
        BodySite=dom-1,dom-2,dom-3,dom-4
        Bundle=bdl-1,bdl-2,bdl-3,bdl-4,bdl-7,bdl-9
        CapabilityStatement=cpb-1,cpb-14,cpb-15,cpb-2,cpb-3,cpb-7,cpb-8,dom-1,dom-2,dom-3,dom-4
        CarePlan=dom-1,dom-2,dom-3,dom-4
        CareTeam=dom-1,dom-2,dom-3,dom-4
        ChargeItem=dom-1,dom-2,dom-3,dom-4
        Claim=dom-1,dom-2,dom-3,dom-4
        ClaimResponse=dom-1,dom-2,dom-3,dom-4
        ClinicalImpression=dom-1,dom-2,dom-3,dom-4
        CodeSystem=csd-1,dom-1,dom-2,dom-3,dom-4
        CodeableConcept=ele-1
        Coding=ele-1
        Communication=com-1,dom-1,dom-2,dom-3,dom-4
        CommunicationRequest=dom-1,dom-2,dom-3,dom-4
        CompartmentDefinition=dom-1,dom-2,dom-3,dom-4
        Composition=dom-1,dom-2,dom-3,dom-4
        ConceptMap=dom-1,dom-2,dom-3,dom-4
        Condition=con-3,con-4,dom-1,dom-2,dom-3,dom-4
        Consent=dom-1,dom-2,dom-3,dom-4,ppc-1
        ContactDetail=ele-1
        ContactPoint=cpt-2,ele-1
        Contract=dom-1,dom-2,dom-3,dom-4
        Contributor=ele-1
        Count=cnt-3,qty-3
        Coverage=dom-1,dom-2,dom-3,dom-4
        DataElement=dom-1,dom-2,dom-3,dom-4
        DataRequirement=ele-1
        DetectedIssue=dom-1,dom-2,dom-3,dom-4
        Device=dom-1,dom-2,dom-3,dom-4
        DeviceComponent=dom-1,dom-2,dom-3,dom-4
        DeviceMetric=dom-1,dom-2,dom-3,dom-4
        DeviceRequest=dom-1,dom-2,dom-3,dom-4
        DeviceUseStatement=dom-1,dom-2,dom-3,dom-4
        DiagnosticReport=dom-1,dom-2,dom-3,dom-4
        Distance=dis-1,qty-3
        DocumentManifest=dom-1,dom-2,dom-3,dom-4
        DocumentReference=dom-1,dom-2,dom-3,dom-4
        DomainResource=dom-1,dom-2,dom-3,dom-4
        Dosage=ele-1
        Duration=drt-1,qty-3
        Element=ele-1
        ElementDefinition=eld-11,eld-13,eld-14,eld-15,eld-16,eld-2,eld-5,eld-6,eld-7,eld-8,ele-1
        EligibilityRequest=dom-1,dom-2,dom-3,dom-4
        EligibilityResponse=dom-1,dom-2,dom-3,dom-4
        Encounter=dom-1,dom-2,dom-3,dom-4
        Endpoint=dom-1,dom-2,dom-3,dom-4
        EnrollmentRequest=dom-1,dom-2,dom-3,dom-4
        EnrollmentResponse=dom-1,dom-2,dom-3,dom-4
        EpisodeOfCare=dom-1,dom-2,dom-3,dom-4
        ExpansionProfile=dom-1,dom-2,dom-3,dom-4
        ExplanationOfBenefit=dom-1,dom-2,dom-3,dom-4
        Extension=ele-1,ext-1
        FamilyMemberHistory=dom-1,dom-2,dom-3,dom-4,fhs-1,fhs-2,fhs-3
        Flag=dom-1,dom-2,dom-3,dom-4
        Goal=dom-1,dom-2,dom-3,dom-4
        GraphDefinition=dom-1,dom-2,dom-3,dom-4
        Group=dom-1,dom-2,dom-3,dom-4,grp-1
        GuidanceResponse=dom-1,dom-2,dom-3,dom-4
        HealthcareService=dom-1,dom-2,dom-3,dom-4
        HumanName=ele-1
        Identifier=ele-1
        ImagingManifest=dom-1,dom-2,dom-3,dom-4
        ImagingStudy=dom-1,dom-2,dom-3,dom-4
        Immunization=dom-1,dom-2,dom-3,dom-4,imm-1,imm-2
        ImmunizationRecommendation=dom-1,dom-2,dom-3,dom-4
        ImplementationGuide=dom-1,dom-2,dom-3,dom-4
        Library=dom-1,dom-2,dom-3,dom-4
        Linkage=dom-1,dom-2,dom-3,dom-4,lnk-1
        List=dom-1,dom-2,dom-3,dom-4,lst-1,lst-2
        Location=dom-1,dom-2,dom-3,dom-4
        Measure=dom-1,dom-2,dom-3,dom-4
        MeasureReport=dom-1,dom-2,dom-3,dom-4
        Media=dom-1,dom-2,dom-3,dom-4,mda-1,mda-2,mda-3,mda-4
        Medication=dom-1,dom-2,dom-3,dom-4
        MedicationAdministration=dom-1,dom-2,dom-3,dom-4,mad-2,mad-3
        MedicationDispense=dom-1,dom-2,dom-3,dom-4,mdd-1
        MedicationRequest=dom-1,dom-2,dom-3,dom-4
        MedicationStatement=dom-1,dom-2,dom-3,dom-4,mst-1
        MessageDefinition=dom-1,dom-2,dom-3,dom-4
        MessageHeader=dom-1,dom-2,dom-3,dom-4
        Meta=ele-1
        Money=mny-1,qty-3
        NamingSystem=dom-1,dom-2,dom-3,dom-4,nsd-1,nsd-2,nsd-3
        Narrative=ele-1
        NutritionOrder=dom-1,dom-2,dom-3,dom-4,nor-1
        Observation=dom-1,dom-2,dom-3,dom-4,obs-6,obs-7
        OperationDefinition=dom-1,dom-2,dom-3,dom-4
        OperationOutcome=dom-1,dom-2,dom-3,dom-4
        Organization=dom-1,dom-2,dom-3,dom-4,org-1
        ParameterDefinition=ele-1
        Patient=dom-1,dom-2,dom-3,dom-4
        PaymentNotice=dom-1,dom-2,dom-3,dom-4
        PaymentReconciliation=dom-1,dom-2,dom-3,dom-4
        Period=ele-1,per-1
        Person=dom-1,dom-2,dom-3,dom-4
        PlanDefinition=dom-1,dom-2,dom-3,dom-4
        Practitioner=dom-1,dom-2,dom-3,dom-4
        PractitionerRole=dom-1,dom-2,dom-3,dom-4
        Procedure=dom-1,dom-2,dom-3,dom-4,pro-1
        ProcedureRequest=dom-1,dom-2,dom-3,dom-4
        ProcessRequest=dom-1,dom-2,dom-3,dom-4
        ProcessResponse=dom-1,dom-2,dom-3,dom-4
        Provenance=dom-1,dom-2,dom-3,dom-4
        Quantity=ele-1,qty-3
        Questionnaire=dom-1,dom-2,dom-3,dom-4,que-2
        QuestionnaireResponse=dom-1,dom-2,dom-3,dom-4
        Range=ele-1,rng-2
        Ratio=ele-1,rat-1
        Reference=ele-1,ref-1
        ReferralRequest=dom-1,dom-2,dom-3,dom-4
        RelatedArtifact=ele-1
        RelatedPerson=dom-1,dom-2,dom-3,dom-4
        RequestGroup=dom-1,dom-2,dom-3,dom-4
        ResearchStudy=dom-1,dom-2,dom-3,dom-4
        ResearchSubject=dom-1,dom-2,dom-3,dom-4
        RiskAssessment=dom-1,dom-2,dom-3,dom-4
        SampledData=ele-1
        Schedule=dom-1,dom-2,dom-3,dom-4
        SearchParameter=dom-1,dom-2,dom-3,dom-4,spd-1,spd-2
        Sequence=dom-1,dom-2,dom-3,dom-4,seq-3
        ServiceDefinition=dom-1,dom-2,dom-3,dom-4
        Signature=ele-1
        Slot=dom-1,dom-2,dom-3,dom-4
        Specimen=dom-1,dom-2,dom-3,dom-4
        StructureDefinition=dom-1,dom-2,dom-3,dom-4,sdf-1,sdf-11,sdf-14,sdf-16,sdf-17,sdf-18,sdf-19,sdf-4,sdf-5,sdf-6,sdf-7,sdf-9
        StructureMap=dom-1,dom-2,dom-3,dom-4
        Subscription=dom-1,dom-2,dom-3,dom-4
        Substance=dom-1,dom-2,dom-3,dom-4
        SupplyDelivery=dom-1,dom-2,dom-3,dom-4
        SupplyRequest=dom-1,dom-2,dom-3,dom-4
        Task=dom-1,dom-2,dom-3,dom-4,inv-1
        TestReport=dom-1,dom-2,dom-3,dom-4
        TestScript=dom-1,dom-2,dom-3,dom-4
        Timing=ele-1
        TriggerDefinition=ele-1
        UsageContext=ele-1
        ValueSet=dom-1,dom-2,dom-3,dom-4,vsd-5
        VisionPrescription=dom-1,dom-2,dom-3,dom-4
        base64Binary=ele-1
        boolean=ele-1
        code=ele-1
        date=ele-1
        dateTime=ele-1
        decimal=ele-1
        id=ele-1
        instant=ele-1
        integer=ele-1
        markdown=ele-1
        oid=ele-1
        positiveInt=ele-1
        string=ele-1
        time=ele-1
        unsignedInt=ele-1
        uri=ele-1
        uuid=ele-1
        """;
}
