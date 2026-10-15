// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary><c>terminology_lookup</c> / <c>cli-on-fhir term lookup</c>: one concept's display, definition, status, properties, designations and parents.</summary>
public static class TermLookup
{
    public static Task<ToolEnvelope<TermLookupFacts?>> LookupAsync(FolderSession session, string? system, string? code, string? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return TerminologyCall.RunAsync<TermLookupFacts>(
            session,
            async state =>
            {
                string checkedSystem = TermArguments.Required(system, "system");
                string checkedCode = TermArguments.Required(code, "code");
                string? checkedVersion = TermArguments.Optional(version, "version");
                TerminologyAnswer<TermLookupFacts> local = Answer(state.Store, checkedSystem, checkedCode, checkedVersion);
                return session.Terminology.Options.TxServer is { } tx && TxFallback.SystemMissing(local.Facts.Outcome)
                    ? await TxAnswers.LookupAsync(tx, checkedSystem, checkedCode, checkedVersion, TxFallback.Why(local.Absent), cancellationToken)
                    : local;
            },
            cancellationToken);
    }

    internal static TerminologyAnswer<TermLookupFacts> Answer(TerminologyStore store, string system, string code, string? version)
    {
        ConceptQuery query = ConceptQuery.Find(store, system, version, code);
        List<OmittedItem> omitted = [];
        List<IssueItem> issues = [.. query.Issues(store, hierarchy: false)];
        if (query.Concept is not { } concept)
        {
            var notFound = new TermLookupFacts(
                query.Outcome,
                system,
                query.Entry?.Version ?? version,
                code,
                query.Source(),
                query.Entry?.Content,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                query.Outcome == CodeSystemResolution.VersionNotLoaded ? [.. query.Resolution.LoadedVersions.Select(entry => entry.Version)] : null);
            return new TerminologyAnswer<TermLookupFacts>(notFound, query.Evidence(), omitted, [query.Absent(store)!], issues);
        }

        CodeSystemEntry entry = query.Entry!;
        if (concept.Inactive)
        {
            issues.Add(new IssueItem("information", "inactive-concept", $"{TerminologyNames.Coded(entry.Url, concept.Code)} is inactive in {TerminologyNames.Canonical(entry.Url, entry.Version)}; see its status and properties for a replacement.", entry.Origin.ToEvidence().File));
        }

        var facts = new TermLookupFacts(
            ConceptQuery.Found,
            system,
            entry.Version,
            concept.Code,
            AnswerSource.Local(entry.Origin),
            entry.Content,
            TermText.Clip(concept.Display, TermText.MaxShortText, "display", omitted),
            TermText.Clip(concept.Definition, TermText.MaxLongText, "definition", omitted),
            concept.Status,
            concept.Inactive,
            TermText.Take(concept.Properties, "properties", omitted),
            TermText.Take(concept.Designations, "designations", omitted),
            [.. concept.Parents.Select(parent => entry.Find(parent)!).Select(parent => new RelatedConcept(parent.Code, TermText.Clip(parent.Display, TermText.MaxShortText, "parent display", omitted), parent.Inactive, 1))],
            concept.Children.Count,
            entry.HierarchyMeaning,
            null);

        // Arguments are the caller's own system and code, never content from the CodeSystem.
        List<NextCall> next = concept.Children.Count > 0
            ? [new NextCall("terminology_navigate", new Dictionary<string, object> { ["system"] = system, ["code"] = code, ["direction"] = "descendants" })]
            : [];
        return new TerminologyAnswer<TermLookupFacts>(facts, [entry.Origin.ToEvidence()], omitted, [], issues) { Next = next };
    }
}
