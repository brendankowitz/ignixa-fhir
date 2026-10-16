// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>Reads resources by reference straight from their recorded file locations.</summary>
public static class ResourceFetcher
{
    /// <summary>Most references read by one call.</summary>
    public const int MaxReferences = 50;

    /// <summary>
    /// Reads <paramref name="references"/> (<c>Type/id</c>), optionally trimmed to <paramref name="elements"/>. A
    /// reference the catalog does not hold is reported in <c>absent</c>. A malformed reference fails the call: the
    /// result is an envelope with null facts and an <c>error</c> issue.
    /// </summary>
    public static ToolEnvelope<ReadFacts?> Read(FolderSession session, IReadOnlyList<string> references, string? elements = null, int? maxBytes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(references);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<ReadFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            return Execute(session, references, elements, maxBytes, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<ReadFacts>(session, ex.Issue, cancellationToken);
        }
    }

    private static ToolEnvelope<ReadFacts?> Execute(FolderSession session, IReadOnlyList<string> references, string? elements, int? maxBytes, CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            throw ToolRequestException.Error("invalid-parameter", "At least one reference is required, for example 'Patient/123'.");
        }

        List<(string Type, string Id)> parsed = [.. references.Select(reference => ParseReference(session, reference)).Distinct()];
        List<OmittedItem> omitted = parsed.Count > MaxReferences
            ? [new OmittedItem($"{parsed.Count - MaxReferences} more references not read (ask for them in a further call)", "limit")]
            : [];
        parsed = [.. parsed.Take(MaxReferences)];

        ElementsProjection? projection = elements is null ? null : ElementsProjection.Parse([elements], session.Schema);
        DateTimeOffset builtAt = session.Catalog.BuiltAt;
        List<CatalogEntry> found = [];
        List<AbsentItem> absent = [];
        foreach ((string type, string id) in parsed)
        {
            if (session.FindByName(type, id) is { } entry)
            {
                // A resource asked for by two names (its id and its digest) is returned once.
                if (!found.Contains(entry))
                {
                    found.Add(entry);
                }
            }
            else
            {
                int ofType = session.Catalog.Entries.Count(candidate => candidate.ResourceType == type);
                absent.Add(new AbsentItem($"{type}/{id}", $"catalog of {session.RootPath}: {ofType} {type} resources in {session.Catalog.FilesScanned} files", builtAt));
            }
        }

        List<IssueItem> issues = [.. DuplicateIdIssues.For(session.Catalog, parsed.Select(reference => reference.Type)), .. NameIssues.For(found)];
        foreach (string type in found.Select(entry => entry.ResourceType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            issues.AddRange(projection?.Validate(type) ?? []);
        }

        int budget = ResponseBudget.Resolve(maxBytes, session.MaxResponseBytes, issues);

        // The listed resolutions can only shrink once the resources are read, so each resource is charged for its candidates.
        Dictionary<string, IReadOnlyDictionary<string, string>> resolutions = Resolutions(session, found);
        Dictionary<string, Dictionary<string, string>> candidates = ResolvedReferences.Candidates(
            found.Select(entry => (ResourceReader.Describe(entry), resolutions[ResourceReader.Describe(entry)])),
            ResolvedReferences.Limit);

        // The closing omitted items name the first reference not returned and count unlisted resolutions, so reserve room for both.
        int longestReference = found.Select(entry => ResourceReader.Describe(entry).Length).DefaultIfEmpty(0).Max();
        // The call in next, which lists the references left out, exists only when the budget cuts the response, so the
        // resources are first read expecting that it does not and, when it does, again with room for that call.
        int Reserved(bool cutBySize) => EnvelopeJson.SizeOf(new ToolEnvelope<ReadFacts?>(
            new ReadFacts(parsed.Count, [], ResolvedReferences: null),
            [],
            [.. omitted, new OmittedItem(UnreadMessage(int.MaxValue, new string('x', longestReference)), "size-budget"), new OmittedItem(NotListedMessage(int.MaxValue), "limit")],
            absent,
            issues,
            new FreshnessInfo(builtAt, StaleFiles: int.MaxValue))
        {
            Next = cutBySize && found.Count > 1 ? Remaining([.. found.Skip(1).Select(ResourceReader.Describe)], elements, maxBytes, budget, session.MaxResponseBytes) : [],
        });
        Func<int, JsonObject, int> costs = ResolvedReferences.Charge([.. found.Select(entry => (ResourceReader.Describe(entry), resolutions[ResourceReader.Describe(entry)]))]);
        ResourceBatch batch = ResourceBatch.Collect(session.RootPath, found, projection, Reserved(cutBySize: false), budget, costs, cancellationToken);
        if (batch.Consumed < found.Count)
        {
            batch = ResourceBatch.Collect(session.RootPath, found, projection, Reserved(cutBySize: true), budget, costs, cancellationToken);
        }

        issues.AddRange(batch.Issues);
        omitted.AddRange(batch.Skipped);
        (Dictionary<string, Dictionary<string, string>> shown, int notListed) = ResolvedReferences.Shown(resolutions, batch, candidates);
        if (notListed > 0)
        {
            omitted.Add(new OmittedItem(NotListedMessage(notListed), "limit"));
        }

        List<NextCall> next = [];
        if (batch.Consumed < found.Count)
        {
            omitted.Add(new OmittedItem(UnreadMessage(found.Count - batch.Consumed, ResourceReader.Describe(found[batch.Consumed])), "size-budget"));
            next = Remaining([.. found.Skip(batch.Consumed).Select(ResourceReader.Describe)], elements, maxBytes, budget, session.MaxResponseBytes);
        }

        return EnvelopeAssembler.Create<ReadFacts?>(session, new ReadFacts(parsed.Count, batch.Resources, ResolvedReferences.ToFacts(shown)), builtAt, batch.Evidence, omitted, absent, issues, cancellationToken) with { Next = next };
    }

    /// <summary>
    /// Each found resource's Bundle references and the <c>Type/id</c> they name, by <c>Type/id</c>, resolved as the index
    /// resolves them (<see cref="Indexing.BundleReferenceResolver"/>, conditional references included) but without building
    /// any index; a resource that cannot be read resolves nothing here and is reported by the batch.
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, string>> Resolutions(FolderSession session, List<CatalogEntry> found)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> resolutions = new(StringComparer.Ordinal);
        foreach (CatalogEntry entry in found)
        {
            resolutions[ResourceReader.Describe(entry)] = ResourceReader.Read(session.RootPath, entry).Resource is { } resource
                ? session.BundleReferences.Rewrite(entry, resource).Resolution.Resolved
                : Indexing.ReferenceResolution.None.Resolved;
        }

        return resolutions;
    }

    /// <summary>
    /// The call that reads the references a size-cut response left out. The references are listed once, so the way to fit
    /// more is a hint on that call rather than a second call repeating them.
    /// </summary>
    private static List<NextCall> Remaining(string[] references, string? elements, int? maxBytes, int budget, int ceiling)
    {
        Dictionary<string, object> args = new(StringComparer.Ordinal) { ["references"] = references };
        if (elements is not null)
        {
            args["elements"] = elements;
        }

        if (maxBytes is int requested)
        {
            args["maxBytes"] = requested;
        }

        string? hint = (elements is null, budget < ceiling) switch
        {
            (true, true) => $"the rest; set elements so more fit, or raise maxBytes (up to {ceiling})",
            (true, false) => "the rest; set elements so more fit",
            (false, true) => $"the rest; raise maxBytes (up to {ceiling})",
            _ => null,
        };
        return [new NextCall("read_resources", args, hint)];
    }

    private static string NotListedMessage(int count) =>
        $"{count} more resolved Bundle references in the returned resources (resolvedReferences lists at most {ResolvedReferences.Limit})";

    /// <summary>The unread resources are the tail of the found list, in request order, so the first one identifies where to resume.</summary>
    private static string UnreadMessage(int count, string first) =>
        $"{count} found resources not returned, from {first} on (request them in a further call)";

    internal static (string Type, string Id) ParseReference(FolderSession session, string reference)
    {
        string[] parts = reference?.Split('/') ?? [];
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw ToolRequestException.Error("invalid-reference", $"'{reference}' is not a relative reference; expected 'ResourceType/id', for example 'Patient/123'.");
        }

        if (!session.Schema.ResourceTypeNames.Contains(parts[0]))
        {
            throw ToolRequestException.Error("unknown-resource-type", $"'{parts[0]}' in '{reference}' is not a {session.Schema.Version} resource type.");
        }

        return (parts[0], parts[1]);
    }
}
