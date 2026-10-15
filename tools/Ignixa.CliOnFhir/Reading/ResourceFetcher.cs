// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
    public static ToolEnvelope<ReadFacts?> Read(FolderSession session, IReadOnlyList<string> references, string? elements = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(references);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<ReadFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            return Execute(session, references, elements, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<ReadFacts>(session, ex.Issue, cancellationToken);
        }
    }

    private static ToolEnvelope<ReadFacts?> Execute(FolderSession session, IReadOnlyList<string> references, string? elements, CancellationToken cancellationToken)
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
            if (session.FindEntry(type, id) is { } entry)
            {
                found.Add(entry);
            }
            else
            {
                int ofType = session.Catalog.Entries.Count(candidate => candidate.ResourceType == type);
                absent.Add(new AbsentItem($"{type}/{id}", $"catalog of {session.RootPath}: {ofType} {type} resources in {session.Catalog.FilesScanned} files", builtAt));
            }
        }

        List<IssueItem> issues = [.. DuplicateIdIssues.For(session.Catalog, parsed.Select(reference => reference.Type)), .. DuplicateIdIssues.InvalidIds(session.Catalog, parsed.Select(reference => reference.Type))];
        foreach (string type in found.Select(entry => entry.ResourceType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            issues.AddRange(projection?.Validate(type) ?? []);
        }

        // The listed resolutions can only shrink once the resources are read, so reserving the candidates keeps the cap.
        Dictionary<string, IReadOnlyDictionary<string, string>> resolutions = Resolutions(session, found);
        Dictionary<string, Dictionary<string, string>> candidates = ResolvedReferences.Candidates(
            found.Select(entry => (ResourceReader.Describe(entry), resolutions[ResourceReader.Describe(entry)])),
            ResolvedReferences.Limit);

        // The closing omitted items name the first reference not returned and count unlisted resolutions, so reserve room for both.
        int longestReference = found.Select(entry => ResourceReader.Describe(entry).Length).DefaultIfEmpty(0).Max();
        int reserved = EnvelopeJson.SizeOf(new ToolEnvelope<ReadFacts?>(
            new ReadFacts(parsed.Count, [], Facts(candidates)),
            [],
            [.. omitted, new OmittedItem(UnreadMessage(int.MaxValue, new string('x', longestReference)), "size-budget"), new OmittedItem(NotListedMessage(int.MaxValue), "limit")],
            absent,
            issues,
            new FreshnessInfo(builtAt, StaleFiles: int.MaxValue)));
        ResourceBatch batch = ResourceBatch.Collect(session.RootPath, found, projection, reserved, cancellationToken);
        issues.AddRange(batch.Issues);
        omitted.AddRange(batch.Skipped);
        (Dictionary<string, Dictionary<string, string>> shown, int notListed) = ResolvedReferences.Shown(resolutions, batch, candidates);
        if (notListed > 0)
        {
            omitted.Add(new OmittedItem(NotListedMessage(notListed), "limit"));
        }

        if (batch.Consumed < found.Count)
        {
            omitted.Add(new OmittedItem(UnreadMessage(found.Count - batch.Consumed, ResourceReader.Describe(found[batch.Consumed])), "size-budget"));
        }

        return EnvelopeAssembler.Create<ReadFacts?>(session, new ReadFacts(parsed.Count, batch.Resources, Facts(shown)), builtAt, batch.Evidence, omitted, absent, issues, cancellationToken);
    }

    /// <summary>
    /// Each found resource's Bundle references and the <c>Type/id</c> they name, by <c>Type/id</c>, resolved as the index
    /// resolves them (<see cref="Indexing.BundleReferenceResolver"/>) but without building any index. A folder without Bundle
    /// entry fullUrls has no Bundle references, so nothing is read for it; a resource that cannot be read resolves nothing
    /// here and is reported by the batch.
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, string>> Resolutions(FolderSession session, List<CatalogEntry> found)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> resolutions = new(StringComparer.Ordinal);
        foreach (CatalogEntry entry in found)
        {
            resolutions[ResourceReader.Describe(entry)] = session.Catalog.FullUrls.Count > 0 && ResourceReader.Read(session.RootPath, entry).Resource is { } resource
                ? session.BundleReferences.Rewrite(entry, resource).Resolution.Resolved
                : Indexing.ReferenceResolution.None.Resolved;
        }

        return resolutions;
    }

    /// <summary>The facts form of a resolved-references map: null when it lists nothing, so the member is left out.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Facts(Dictionary<string, Dictionary<string, string>> resolved) =>
        resolved.Count == 0 ? null : resolved.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, string>)pair.Value, StringComparer.Ordinal);

    private static string NotListedMessage(int count) =>
        $"{count} more resolved Bundle references in the returned resources (resolvedReferences lists at most {ResolvedReferences.Limit})";

    /// <summary>The unread resources are the tail of the found list, in request order, so the first one identifies where to resume.</summary>
    private static string UnreadMessage(int count, string first) =>
        $"{count} found resources not returned, from {first} on (request them in a further call)";

    private static (string Type, string Id) ParseReference(FolderSession session, string reference)
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
