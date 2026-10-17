// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Patients;

/// <summary><c>patient_context</c> root: the deterministic digest of one patient's context, one stub per domain.</summary>
public static class ContextRoot
{
    /// <summary>
    /// The root digest of <c>Patient/<paramref name="patientId"/></c>, as of <paramref name="asOf"/>, else as of the patient's
    /// latest dated resource (<see cref="PatientContext.Open"/>). A request that cannot be answered (unreadable folder,
    /// malformed id, unknown patient) yields null facts and an <c>error</c> issue.
    /// </summary>
    public static ToolEnvelope<RootDigestFacts?> Get(FolderSession session, string patientId, DateTimeOffset? asOf = null, CancellationToken cancellationToken = default) =>
        Answer(session, () => PatientContext.Open(session, patientId, asOf), cancellationToken);

    /// <summary>
    /// The root digest as of what <paramref name="asOf"/> was stated (the session's <c>--as-of</c>), else as of the patient's
    /// latest dated resource: <c>patient_context</c> root <c>detail=full</c> and <c>context</c>.
    /// </summary>
    public static ToolEnvelope<RootDigestFacts?> Get(FolderSession session, string patientId, AsOfClock asOf, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asOf);

        return Answer(session, () => PatientContext.Open(session, patientId, asOf.Stated(argument: null)), cancellationToken);
    }

    private static ToolEnvelope<RootDigestFacts?> Answer(FolderSession session, Func<PatientContext> open, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<RootDigestFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            return Execute(open(), cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<RootDigestFacts>(session, ex.Issue, cancellationToken);
        }
    }

    private static ToolEnvelope<RootDigestFacts?> Execute(PatientContext context, CancellationToken cancellationToken)
    {
        FolderSession session = context.Session;
        List<ContextEntry> shown = [];
        DomainStub problems = RuleStub(context, ClinicalDomains.Problems, context.Problems, PatientContext.ActiveProblemRule, shown);
        DomainStub medications = RuleStub(context, ClinicalDomains.Medications, context.Medications, PatientContext.ActiveMedicationRule, shown);
        DomainStub allergies = Stub(ClinicalDomains.Allergies, context.Allergies, context.Allergies.Count, PatientContext.AllergyRule, excluded: null, window: null, shown);
        DomainStub encounters = Stub(
            ClinicalDomains.Encounters,
            context.Encounters,
            context.Encounters.Count,
            context.EncounterRuleApplied,
            context.MembersOf("Encounter").Count - context.Encounters.Count,
            context.Window,
            shown);
        DomainStub labs = Stub(
            ClinicalDomains.Labs,
            [.. context.Labs.Select(series => series.Summary)],
            context.Labs.Count,
            context.LabRuleApplied,
            context.LabsExcluded,
            context.Window,
            shown);

        List<AbsentItem> absent = [.. shown.SelectMany(entry => entry.Absent).DistinctBy(item => item.What, StringComparer.Ordinal)];
        if (context.Allergies.Count == 0)
        {
            absent.Add(AllergyAbsence(context));
        }

        Dictionary<string, int> otherTypes = context.Members
            .Where(member => !ClinicalDomains.StubTypes.Contains(member.ResourceType))
            .GroupBy(member => member.ResourceType, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var facts = new RootDigestFacts(
            context.PatientReference,
            session.Schema.Version.ToString(),
            context.AsOf,
            context.Members.Count,
            context.Demographics(),
            problems,
            medications,
            allergies,
            encounters,
            labs,
            otherTypes,
            context.Shapes.Membership);

        List<EvidenceItem> evidence = Evidence([context.Compartment.Patient, .. shown.SelectMany(entry => entry.Sources)]);
        List<OmittedItem> omitted = [];
        List<IssueItem> issues = context.Issues(omitted, shown.SelectMany(entry => entry.Sources));
        List<NextCall> next =
        [
            .. new[] { problems, medications, allergies, encounters, labs }
                .Where(stub => stub.Count > 0 || stub.Excluded > 0)
                .Select(stub => Expand(context, stub.NodeKey)),
            .. otherTypes.Keys.Where(_ => FhirIds.IsValid(context.Compartment.Patient.Id)).Select(type => new NextCall("search_fhir_resources", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["resourceType"] = type,
                ["compartment"] = context.PatientReference,
            })),
        ];

        DateTimeOffset builtAt = context.Compartment.IndexBuiltAt;
        return EnvelopeAssembler.Create<RootDigestFacts?>(session, facts, builtAt, evidence, omitted, absent, issues, context.Anchor, cancellationToken) with { Next = next };
    }

    /// <summary>The <c>patient_context</c> expand call for <paramref name="nodeKey"/> of this patient.</summary>
    internal static NextCall Expand(PatientContext context, string nodeKey, int? offset = null)
    {
        Dictionary<string, object> args = new(StringComparer.Ordinal)
        {
            ["operation"] = "expand",
            ["patientId"] = context.PatientId,
            ["nodeKey"] = nodeKey,
        };
        if (offset is int value)
        {
            args["offset"] = value;
        }

        return new NextCall("patient_context", args);
    }

    /// <summary>The evidence items of <paramref name="entries"/>, in order, each resource once, named as summaries name it (<see cref="FhirIds.Reference(CatalogEntry)"/>).</summary>
    internal static List<EvidenceItem> Evidence(IEnumerable<CatalogEntry> entries) =>
        [.. entries
            .DistinctBy(entry => (entry.ResourceType, entry.Id))
            .Select(entry => new EvidenceItem(FhirIds.Reference(entry), entry.FilePath, entry.Line, entry.Offset))];

    /// <summary>The absence of allergies, with the compartment and the folder's AllergyIntolerance resources it checked.</summary>
    internal static AbsentItem AllergyAbsence(PatientContext context)
    {
        FolderSession session = context.Session;
        IReadOnlyList<string> through = session.Compartments.Parameters("Patient", "AllergyIntolerance");
        int inFolder = session.Catalog.Entries.Count(entry => entry.ResourceType == "AllergyIntolerance");
        DateTimeOffset builtAt = context.Compartment.IndexBuiltAt;
        return new AbsentItem(
            "AllergyIntolerance",
            $"{context.PatientReference} compartment ({context.Members.Count} resources{(context.AnyAfterAsOf ? " at or before asOf" : string.Empty)}, via {string.Join(", ", through)}); {inFolder} in the folder's {session.Catalog.FilesScanned} files. No record, not 'no known allergies'",
            builtAt);
    }

    /// <summary>A stub for a domain with an active rule: its active entries when the rule was applied, else every entry.</summary>
    private static DomainStub RuleStub(PatientContext context, string nodeKey, IReadOnlyList<ContextEntry> entries, string rule, List<ContextEntry> shown)
    {
        if (!context.RulesApplied)
        {
            return Stub(nodeKey, entries, entries.Count, rule: null, excluded: null, window: null, shown);
        }

        // Every item shown is active, which the rule says once, so the per-item flag is dropped.
        List<ContextEntry> active = [.. entries.Where(entry => entry.Item.Active == true).Select(entry => entry with { Item = entry.Item with { Active = null } })];
        return Stub(nodeKey, active, active.Count, rule, entries.Count - active.Count, window: null, shown);
    }

    private static DomainStub Stub(string nodeKey, IReadOnlyList<ContextEntry> entries, int count, string? rule, int? excluded, ContextWindow? window, List<ContextEntry> shown)
    {
        List<ContextEntry> top = [.. entries.Take(PatientContext.TopItems)];
        shown.AddRange(top);
        return new DomainStub(nodeKey, count, [.. top.Select(entry => entry.Item)], rule, excluded, window);
    }
}
