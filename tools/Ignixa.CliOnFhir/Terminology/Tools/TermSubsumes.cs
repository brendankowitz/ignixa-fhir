// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>
/// <c>terminology_subsumes</c> / <c>cli-on-fhir term subsumes</c>: subsumption between two codes of one system, from the
/// loaded hierarchy. <c>not-subsumed</c> is answered only when both codes are in a complete CodeSystem and neither is an
/// ancestor of the other; a system that is not loaded, a missing code or incomplete content is never <c>not-subsumed</c>,
/// and a hierarchy declared as other than <c>is-a</c> answers <c>unknown</c> for distinct codes.
/// </summary>
public static class TermSubsumes
{
    public const string Equivalent = "equivalent";
    public const string Subsumes = "subsumes";
    public const string SubsumedBy = "subsumed-by";
    public const string NotSubsumed = "not-subsumed";
    public const string Unknown = "unknown";

    public static Task<ToolEnvelope<TermSubsumesFacts?>> SubsumesAsync(FolderSession session, string? system, string? codeA, string? codeB, string? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return TerminologyCall.RunAsync<TermSubsumesFacts>(
            session,
            async state =>
            {
                string checkedSystem = TermArguments.Required(system, "system");
                string a = TermArguments.Required(codeA, "codeA");
                string b = TermArguments.Required(codeB, "codeB");
                string? checkedVersion = TermArguments.Optional(version, "version");
                TerminologyAnswer<TermSubsumesFacts> local = Answer(state.Store, checkedSystem, a, b, checkedVersion);
                return session.Terminology.Options.TxServer is { } tx && TxFallback.SystemMissing(local.Facts.Outcome)
                    ? await TxAnswers.SubsumesAsync(tx, checkedSystem, a, b, checkedVersion, TxFallback.Why(local.Absent), cancellationToken)
                    : local;
            },
            cancellationToken);
    }

    internal static TerminologyAnswer<TermSubsumesFacts> Answer(TerminologyStore store, string system, string codeA, string codeB, string? version)
    {
        ConceptQuery a = ConceptQuery.Find(store, system, version, codeA);
        ConceptQuery b = ConceptQuery.Find(store, system, version, codeB);
        List<IssueItem> issues = [.. a.Issues(store, hierarchy: true)];
        if (a.Entry is not { } entry)
        {
            var notLoaded = new TermSubsumesFacts(a.Outcome, system, version, codeA, codeB, null, null, []);
            return new TerminologyAnswer<TermSubsumesFacts>(notLoaded, [], [], [a.Absent(store)!], issues);
        }

        List<string> missing = [.. new[] { a, b }.Where(query => query.Concept is null).Select(query => query.Code).Distinct(StringComparer.Ordinal)];
        if (missing.Count > 0)
        {
            string outcome = entry.IsComplete ? ConceptQuery.CodeNotInVersion : ConceptQuery.CodeNotInLoadedContent;
            var notFound = new TermSubsumesFacts(outcome, system, entry.Version, codeA, codeB, AnswerSource.Local(entry.Origin), entry.HierarchyMeaning, missing);
            return new TerminologyAnswer<TermSubsumesFacts>(notFound, [entry.Origin.ToEvidence()], [], [.. new[] { a, b }.Where(query => query.Concept is null).DistinctBy(query => query.Code).Select(query => query.Absent(store)!)], issues);
        }

        Concept conceptA = a.Concept!;
        Concept conceptB = b.Concept!;
        string relation = conceptA.Code == conceptB.Code
            ? Equivalent
            : entry.AncestorsOf(conceptB).Contains(conceptA.Code)
                ? Subsumes
                : entry.AncestorsOf(conceptA).Contains(conceptB.Code) ? SubsumedBy : NotSubsumed;

        // A hierarchy that is not is-a (grouped-by, part-of, classified-with) says nothing about subsumption.
        if (relation != Equivalent && entry.HierarchyMeaning is { } meaning && meaning != "is-a")
        {
            relation = Unknown;
        }

        // An unrelated pair is only known to be unrelated when the hierarchy is the whole system.
        if (relation == NotSubsumed && !entry.IsComplete)
        {
            relation = Unknown;
            issues.Add(new IssueItem("warning", "hierarchy-incomplete", $"Neither code is an ancestor of the other in CodeSystem {TerminologyNames.Canonical(entry.Url, entry.Version)}, but its content is '{TerminologyNames.Token(entry.Content ?? "(none)")}', not complete, so they may still be related.", entry.Origin.ToEvidence().File));
        }

        var facts = new TermSubsumesFacts(relation, system, entry.Version, conceptA.Code, conceptB.Code, AnswerSource.Local(entry.Origin), entry.HierarchyMeaning, []);
        return new TerminologyAnswer<TermSubsumesFacts>(facts, [entry.Origin.ToEvidence()], [], [], issues);
    }
}
