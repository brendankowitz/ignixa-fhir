// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary><c>terminology</c> navigate / <c>cli-on-fhir term tree</c>: a concept's ancestors or descendants, by depth, paged.</summary>
public static class TermNavigate
{
    public const int DefaultDepth = 1;

    public const int MaxDepth = 50;

    public static Task<ToolEnvelope<TermNavigateFacts?>> NavigateAsync(FolderSession session, string? system, string? code, string? direction, int? depth, int? count, int offset, string? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return TerminologyCall.RunAsync(
            session,
            state => Answer(state.Store, TermArguments.Required(system, "system"), TermArguments.Required(code, "code"), direction, depth, count, offset, TermArguments.Optional(version, "version")),
            cancellationToken);
    }

    private static TerminologyAnswer<TermNavigateFacts> Answer(TerminologyStore store, string system, string code, string? direction, int? depth, int? count, int offset, string? version)
    {
        string way = (direction?.Trim()) switch
        {
            null or "" or "descendants" => "descendants",
            "ancestors" => "ancestors",
            _ => throw ToolRequestException.Error("invalid-parameter", "'direction' is 'descendants' (the default) or 'ancestors'."),
        };
        int levels = depth ?? DefaultDepth;
        if (levels is < 1 or > MaxDepth)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'depth' must be between 1 and {MaxDepth}, not {levels}.");
        }

        List<IssueItem> issues = [];
        int pageSize = Paging.Resolve(count, offset, issues);
        ConceptQuery query = ConceptQuery.Find(store, system, version, code);
        issues.AddRange(query.Issues(store, hierarchy: true));
        if (query.Concept is not { } concept)
        {
            var notFound = new TermNavigateFacts(query.Outcome, system, query.Entry?.Version ?? version, code, null, way, levels, query.Source(), 0, offset, 0, []);
            return new TerminologyAnswer<TermNavigateFacts>(notFound, query.Evidence(), [], [query.Absent(store)!], issues);
        }

        CodeSystemEntry entry = query.Entry!;
        List<RelatedConcept> reached = [.. entry.Walk(concept, up: way == "ancestors", levels)
            .Select(step => new RelatedConcept(step.Concept.Code, step.Concept.Display is { } display ? display[..Math.Min(display.Length, TermText.MaxShortText)] : null, step.Concept.Inactive, step.Distance))];

        List<OmittedItem> omitted = [];
        List<RelatedConcept> page = [.. reached.Skip(offset).Take(pageSize)];
        (TermNavigateFacts facts, int kept) = TermText.FitPage(
            page,
            items => new TermNavigateFacts(ConceptQuery.Found, system, entry.Version, concept.Code, concept.Display, way, levels, AnswerSource.Local(entry.Origin), reached.Count, offset, items.Count, items),
            "concepts",
            omitted);
        issues.AddRange(Paging.BeyondTotal(offset, reached.Count, "concepts"));

        List<NextCall> next = [];
        bool echoable = TermNext.Echoable(system, version) && TerminologyNames.Code(code) == code;
        int remaining = reached.Count - offset - kept;
        if (remaining > 0)
        {
            omitted.Add(new OmittedItem($"{remaining} more concepts", "limit"));
            if (echoable)
            {
                next.Add(TermNext.Call("navigate", Args(system, code, way, levels, version, offset + kept, pageSize)));
            }
        }
        else if (levels < MaxDepth && reached.Exists(step => step.Distance == levels) && entry.Walk(concept, up: way == "ancestors", levels + 1).Count > reached.Count)
        {
            // More levels exist beyond the depth asked for: say so, and how to read them.
            omitted.Add(new OmittedItem($"{way} deeper than {levels} levels", "depth"));
            if (echoable)
            {
                next.Add(TermNext.Call("navigate", Args(system, code, way, levels + 1, version, 0, pageSize)));
            }
        }

        List<AbsentItem> absent = reached.Count == 0
            ? [new AbsentItem($"{way} of {TerminologyNames.Coded(system, concept.Code)}", $"the hierarchy of CodeSystem {TerminologyNames.Canonical(entry.Url, entry.Version)} from {entry.Origin.Source.Name} ({entry.InOrder.Count} concepts, content '{TerminologyNames.Token(entry.Content ?? "(none)")}')", store.BuiltAt)]
            : [];
        return new TerminologyAnswer<TermNavigateFacts>(facts, [entry.Origin.ToEvidence()], omitted, absent, issues) { Next = next };
    }

    // Arguments are the caller's own system, code and version plus paging, never CodeSystem content.
    private static Dictionary<string, object> Args(string system, string code, string direction, int depth, string? version, int offset, int count)
    {
        var args = new Dictionary<string, object> { ["system"] = system, ["code"] = code, ["direction"] = direction, ["depth"] = depth, ["offset"] = offset, ["count"] = count };
        if (version is not null)
        {
            args["version"] = version;
        }

        return args;
    }
}
