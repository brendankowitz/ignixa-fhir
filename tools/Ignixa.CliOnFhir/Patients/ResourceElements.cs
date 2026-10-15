// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// Catalogued resources as FHIRPath input for one call: sliced from their file (each file read once per call) and checked
/// to still be the catalogued resource, then converted with the session's schema. With
/// <paramref name="resolveReferences"/>, Bundle references are first rewritten to the <c>Type/id</c> the index resolved them
/// to, so <c>reasonReference.reference</c> reads <c>Condition/c1</c> where the file says <c>urn:uuid:...</c>. Each resource is
/// read at most once. The JSON returned to the agent is read separately and keeps the file's references. Not thread-safe.
/// </summary>
/// <param name="session">The folder.</param>
/// <param name="resolveReferences">Whether FHIRPath must see resolved references; false only where no rule reads a reference.</param>
internal sealed class ResourceElements(FolderSession session, bool resolveReferences = true)
{
    private readonly Dictionary<(string ResourceType, string Id), IElement?> _elements = [];
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly List<IssueItem> _problems = [];

    /// <summary>Resources that could not be read (changed or removed since they were catalogued), one issue each.</summary>
    public IReadOnlyList<IssueItem> Problems => _problems;

    /// <summary>The resource as an element, or null when it cannot be read now; the reason is in <see cref="Problems"/>.</summary>
    public IElement? Get(CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_elements.TryGetValue((entry.ResourceType, entry.Id), out IElement? cached))
        {
            return cached;
        }

        IElement? element = Read(entry);
        _elements.Add((entry.ResourceType, entry.Id), element);
        return element;
    }

    // Parsed from the bytes as the index builder parses them (FolderIndexBuilder.TryIndex): converting an already parsed
    // JsonNode would serialize and parse it again.
    private IElement? Read(CatalogEntry entry)
    {
        if (File(entry) is not { } file)
        {
            return null;
        }

        (ReadOnlyMemory<byte>? json, IssueItem? problem) = ResourceReader.Slice(file, entry);
        if (json is not { } bytes)
        {
            _problems.Add(problem!);
            return null;
        }

        ResourceJsonNode node;
        try
        {
            node = JsonSourceNodeFactory.Parse(bytes);
        }
        catch (JsonException ex)
        {
            _problems.Add(new IssueItem("error", "invalid-resource", $"{ResourceReader.Describe(entry)}: {ex.Message}", entry.Line is int line ? $"{entry.FilePath}:{line}" : entry.FilePath));
            return null;
        }

        if (resolveReferences)
        {
            _ = session.BundleReferences.Rewrite(entry, ((IMutableJsonNode)node).MutableNode);
        }

        return node.ToElement(session.Schema);
    }

    /// <summary>The bytes of <paramref name="entry"/>'s file, read on first use; null (and a problem) when it cannot be read.</summary>
    private byte[]? File(CatalogEntry entry)
    {
        if (_files.TryGetValue(entry.FilePath, out byte[]? file))
        {
            return file;
        }

        try
        {
            file = System.IO.File.ReadAllBytes(Path.Combine(session.RootPath, entry.FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _problems.Add(ResourceReader.Unreadable(entry, ex));
            return null;
        }

        _files.Add(entry.FilePath, file);
        return file;
    }
}
