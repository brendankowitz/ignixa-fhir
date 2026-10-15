// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
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

        try
        {
            return Execute(session, references, elements, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Create<ReadFacts?>(session, null, session.Catalog.BuiltAt, [], [], [], [ex.Issue], cancellationToken);
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

        List<IssueItem> issues = [];
        foreach (string type in found.Select(entry => entry.ResourceType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            issues.AddRange(projection?.Validate(type) ?? []);
        }

        ResourceBatch collection = ResourceBatch.Collect(session.RootPath, found, projection, cancellationToken);
        issues.AddRange(collection.Issues);
        if (collection.Consumed < found.Count)
        {
            IEnumerable<string> unread = found.Skip(collection.Consumed).Select(ResourceReader.Describe);
            omitted.Add(new OmittedItem($"{found.Count - collection.Consumed} found resources not returned: {string.Join(", ", unread)}", "size-budget"));
        }

        return EnvelopeAssembler.Create<ReadFacts?>(session, new ReadFacts(parsed.Count, collection.Resources), builtAt, collection.Evidence, omitted, absent, issues, cancellationToken);
    }

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
