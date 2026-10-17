// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.Patients;

/// <summary><c>patient_context</c> expand: one node of a patient's context, paged, with the page's resources.</summary>
public static class ContextExpander
{
    /// <summary>
    /// Expands <paramref name="nodeKey"/> in <c>Patient/<paramref name="patientId"/></c>'s context as of the session's
    /// <c>--as-of</c>, else as of the patient's latest dated resource. A malformed key, or one that names no node of this patient, fails the call (null facts, an
    /// <c>error</c> issue), as do a malformed id, an unknown patient and bad paging.
    /// </summary>
    public static ToolEnvelope<NodeFacts?> Expand(
        FolderSession session,
        string patientId,
        string nodeKey,
        int? count = null,
        int offset = 0,
        string? elements = null,
        int? maxBytes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<NodeFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            NodeKey.Validate(nodeKey);
            List<IssueItem> issues = [];
            int pageSize = Paging.Resolve(count, offset, issues);
            ElementsProjection? projection = elements is null ? null : ElementsProjection.Parse([elements], session.Schema);
            PatientContext context = PatientContext.Open(session, patientId, session.AsOfClock.Stated(argument: null));
            Node node = Resolve(context, nodeKey, issues);
            List<AbsentItem> absent = node.Entries.Count == 0 && nodeKey == ClinicalDomains.Allergies ? [ContextRoot.AllergyAbsence(context)] : [];
            return ContextPage.Build(
                context,
                node.Entries,
                offset,
                pageSize,
                projection,
                elements,
                maxBytes,
                node.WithResources,
                contents => new NodeFacts(context.PatientReference, nodeKey, context.AsOf, node.Entries.Count, offset, contents.Items.Count, contents.Items, context.Shapes.Membership, node.Rule, node.Window, contents.Resources, contents.ResolvedReferences),
                resume => ContextRoot.Expand(context, nodeKey, resume),
                issues,
                absent,
                cancellationToken) with { AsOf = context.Anchor };
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<NodeFacts>(session, ex.Issue, cancellationToken);
        }
    }

    private static Node Resolve(PatientContext context, string nodeKey, List<IssueItem> issues) => nodeKey switch
    {
        ClinicalDomains.Problems => new(context.Problems, context.RulesApplied ? PatientContext.ActiveProblemRule : null),
        ClinicalDomains.Medications => new(context.Medications, context.RulesApplied ? PatientContext.ActiveMedicationRule : null),
        ClinicalDomains.Allergies => new(context.Allergies, PatientContext.AllergyRule),
        ClinicalDomains.Encounters => new(context.Encounters, context.EncounterRuleApplied, context.Window),
        ClinicalDomains.Labs => new([.. context.Labs.Select(series => series.Summary)], context.LabRuleApplied, context.Window, WithResources: false),
        _ when nodeKey.StartsWith(NodeKey.ProblemPrefix, StringComparison.Ordinal) => Problem(context, nodeKey),
        _ when nodeKey.StartsWith(NodeKey.EncounterPrefix, StringComparison.Ordinal) => Encounter(context, nodeKey, issues),
        _ => Lab(context, nodeKey),
    };

    /// <summary>The Condition, then the medications linked to it (see <see cref="PatientContext.ProblemLinkRule"/>).</summary>
    private static Node Problem(PatientContext context, string nodeKey)
    {
        ContextEntry condition = context.Problems.FirstOrDefault(entry => entry.Item.NodeKey == nodeKey) ?? throw Unknown(context, nodeKey);
        // Matched against the reference as resolved, so by the Condition's own id; the item's name may be its digest.
        string reference = $"Condition/{condition.Resource.Id}";
        List<CodedConcept> codes = [.. CodedConcept.MainCodes("Condition", context.Element(condition)!)];
        List<ContextEntry> linked = [];
        foreach (ContextEntry medication in context.Medications)
        {
            IElement element = context.Element(medication)!;
            string? linkedBy = element.Select(ClinicalRules.ReasonReferences).Any(found => found.Value as string == reference) ? "reasonReference"
                : element.Select(ClinicalRules.ReasonCodes).Select(CodedConcept.Read).Any(reason => SharesCode(reason, codes)) ? "reasonCode"
                : null;
            if (linkedBy is not null)
            {
                linked.Add(medication with { Item = medication.Item with { LinkedBy = linkedBy } });
            }
        }

        return new([condition, .. linked], PatientContext.ProblemLinkRule);
    }

    /// <summary>The Encounter, then the resources in its Encounter compartment, type by type.</summary>
    private static Node Encounter(PatientContext context, string nodeKey, List<IssueItem> issues)
    {
        CatalogEntry encounter = context.MembersOf("Encounter").FirstOrDefault(resource => NodeKey.Encounter(resource.Id) == nodeKey)
            ?? throw Unknown(context, nodeKey);
        (IReadOnlyList<IndexedResource> members, IReadOnlyList<string> unevaluable) = context.Compartment.Folder.CompartmentMembers("Encounter", encounter.Id);
        if (unevaluable.Count > 0)
        {
            issues.Add(new IssueItem("warning", PatientCompartment.UnevaluableCode, $"{string.Join(", ", unevaluable)} resources in the folder were not considered for {FhirIds.Reference("Encounter", encounter.Id)}'s compartment: the CompartmentDefinition lists codes for them that are not reference search parameters.", Location: null));
        }

        IReadOnlyList<ContextEntry> entries = context.Navigable(context.Describe([encounter, .. members.Select(member => member.Entry).Where(member => member != encounter)]));
        return new(entries, $"{FhirIds.Reference("Encounter", encounter.Id)}, then every resource in its Encounter compartment (by the {context.Session.Schema.Version} CompartmentDefinition), type by type in catalog order");
    }

    /// <summary>Every observation of one lab code in the window, newest first.</summary>
    private static Node Lab(PatientContext context, string nodeKey)
    {
        LabSeries series = context.Labs.FirstOrDefault(lab => lab.NodeKey == nodeKey) ?? throw Unknown(context, nodeKey);
        return new([.. series.Observations], context.LabRuleApplied, context.Window);
    }

    private static bool SharesCode(CodedConcept reason, List<CodedConcept> codes) =>
        reason.Codings.Any(coding => coding.System is not null && coding.Code is not null && codes.Any(code => code.HasCode(coding.System, coding.Code)));

    private static ToolRequestException Unknown(PatientContext context, string nodeKey) => ToolRequestException.Error(
        "unknown-node-key",
        $"'{nodeKey}' names no node in {context.PatientReference}'s context as of {context.AsOf:O}. Use a node key patient_context returned for this patient.");

    /// <summary>A resolved node.</summary>
    /// <param name="Entries">Its items, in order.</param>
    /// <param name="Rule">The rule that decided them, when one applies.</param>
    /// <param name="Window">The time window, for a time-dependent node.</param>
    /// <param name="WithResources">Whether the page carries its resources.</param>
    private sealed record Node(IReadOnlyList<ContextEntry> Entries, string? Rule, ContextWindow? Window = null, bool WithResources = true);
}
