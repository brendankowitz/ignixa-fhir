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

namespace Ignixa.CliOnFhir.Patients;

/// <summary><c>patient_context</c> find: the resources of a patient's context whose main code matches a code or a text, with their node keys.</summary>
public static class ContextFinder
{
    /// <summary>Longest query accepted.</summary>
    public const int MaxQueryLength = 256;

    /// <summary>
    /// Finds <paramref name="query"/> in <c>Patient/<paramref name="patientId"/></c>'s context: <c>system|code</c> matches codes
    /// only; a bare token (no whitespace) matches codes in any system first, then texts; anything else matches texts only. A
    /// malformed request yields null facts and an <c>error</c> issue.
    /// </summary>
    public static ToolEnvelope<FindFacts?> Find(
        FolderSession session,
        string patientId,
        string query,
        int? count = null,
        int offset = 0,
        string? elements = null,
        int? maxBytes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<FindFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            string trimmed = Validate(query);
            List<IssueItem> issues = [];
            int pageSize = Paging.Resolve(count, offset, issues);
            ElementsProjection? projection = elements is null ? null : ElementsProjection.Parse([elements], session.Schema);
            PatientContext context = PatientContext.Open(session, patientId, session.AsOfClock.Stated(argument: null));
            (IReadOnlyList<ContextEntry> matches, string rule) = Match(context, trimmed);
            List<AbsentItem> absent = matches.Count == 0 ? [Absence(context, query)] : [];
            return ContextPage.Build(
                context,
                matches,
                offset,
                pageSize,
                projection,
                elements,
                maxBytes,
                withResources: true,
                contents => new FindFacts(
                    context.PatientReference,
                    query,
                    rule,
                    matches.Count,
                    offset,
                    contents.Items.Count,
                    [.. contents.Items.Select(item => item.NodeKey).OfType<string>().Distinct(StringComparer.Ordinal)],
                    contents.Items,
                    context.Shapes.Membership,
                    contents.Resources ?? [],
                    contents.ResolvedReferences),
                resume => Next(context, query, resume),
                issues,
                absent,
                cancellationToken) with { AsOf = context.Anchor };
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<FindFacts>(session, ex.Issue, cancellationToken);
        }
    }

    private static string Validate(string? query)
    {
        string trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxQueryLength)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'query' must be a code ('system|code' or a bare code) or a text of 1 to {MaxQueryLength} characters.");
        }

        return trimmed;
    }

    private static (IReadOnlyList<ContextEntry> Matches, string Rule) Match(PatientContext context, string query)
    {
        List<CatalogEntry> coded = [.. context.Members.Where(member => ClinicalRules.MainCode.ContainsKey(member.ResourceType))];
        int bar = query.IndexOf('|', StringComparison.Ordinal);
        bool byCode = bar >= 0 || !query.Any(char.IsWhiteSpace);
        bool byText = bar < 0;
        (string? system, string code) = bar >= 0 ? (query[..bar], query[(bar + 1)..]) : ((string?)null, query);
        if (code.Length == 0)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'{QueryEcho.Value(query)}' names no code after '|'; use 'system|code', '|code' (no system) or a bare code.");
        }

        List<ContextEntry> codeMatches = byCode ? Where(context, coded, concept => concept.HasCode(system, code), "code") : [];
        HashSet<CatalogEntry> matched = [.. codeMatches.Select(entry => entry.Resource)];
        List<ContextEntry> textMatches = byText
            ? Where(context, coded.Where(member => !matched.Contains(member)), concept => concept.Mentions(query), "text")
            : [];

        string mode = (byCode, byText) switch
        {
            (true, true) => "code (any system), then text",
            (true, false) => system!.Length == 0 ? "code (without a system)" : "code (in that system)",
            _ => "text",
        };
        string rule = $"{mode} against each resource's main code ({string.Join(", ", ClinicalRules.MainCode.Select(pair => $"{pair.Key}.{pair.Value}"))}); "
            + "text is a case-insensitive substring of the code's text or a coding display; code matches first, then text, each in compartment order";
        return (context.Navigable([.. codeMatches, .. textMatches]), rule);
    }

    private static List<ContextEntry> Where(PatientContext context, IEnumerable<CatalogEntry> resources, Func<CodedConcept, bool> predicate, string matchedBy) =>
        [.. context.Describe(resources)
            .Where(entry => CodedConcept.MainCodes(entry.Resource.ResourceType, context.Element(entry)!).Any(predicate))
            .Select(entry => entry with { Item = entry.Item with { MatchedBy = matchedBy } })];

    private static AbsentItem Absence(PatientContext context, string query)
    {
        List<string> types = [.. context.Members.Select(member => member.ResourceType).Distinct(StringComparer.Ordinal).Where(ClinicalRules.MainCode.ContainsKey).Order(StringComparer.Ordinal)];
        int searched = context.Members.Count(member => ClinicalRules.MainCode.ContainsKey(member.ResourceType));
        DateTimeOffset builtAt = context.Compartment.IndexBuiltAt;
        return new AbsentItem(
            $"resources matching '{query}'",
            $"main codes of the {searched} {(types.Count == 0 ? "coded" : string.Join(", ", types))} resources in {context.PatientReference}'s compartment",
            builtAt);
    }

    private static NextCall Next(PatientContext context, string query, int offset) => new("patient_context", new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["operation"] = "find",
        ["patientId"] = context.PatientId,
        ["query"] = query,
        ["offset"] = offset,
    });
}
