// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Search.Serialization;

namespace Ignixa.CliOnFhir.Caching;

/// <summary>
/// The persisted form of one resource type's index, a binary file: a header (magic, layout, the manifest it was written under,
/// the type, its catalogued count, build time and diagnostics), the resources in segments of at most
/// <see cref="SegmentSize"/> (each resource's id, entries as parameter code and value, <see cref="SearchValueBinaryWriter"/>,
/// and Bundle reference resolution), a table of the segments' offsets and counts, the table's offset and an end marker.
/// </summary>
/// <remarks>
/// Replaced the JSON form at V17: MIMIC's 813K Observations were 1.5 GB of JSON that took 18 s to parse, longer than building
/// the index from the data; the binary file is 190 MB. Strings are written once per segment and named by position after, so a
/// reload shares one instance per distinct system, code, reference target or date text within a segment. Segments are
/// independent, so they are read in parallel from one memory map of the file (a file replaced meanwhile by another process is
/// not mixed in) and joined in order: the result is the same index, in the same order, as a sequential read. Reading checks
/// everything the JSON form checked (the manifest, the type, the catalogued count, that each id is catalogued and appears
/// once, that each parameter is defined); anything else that is not what the writer wrote fails as
/// <see cref="InvalidDataException"/> or <see cref="EndOfStreamException"/>.
/// </remarks>
internal static class TypeIndexFile
{
    public const string Suffix = ".index.bin";

    /// <summary>Most resources in one segment, the unit read in parallel.</summary>
    public const int SegmentSize = 32 * 1024;

    /// <summary>Layout version of the file, inside the cache format version (<see cref="CacheManifest.CurrentFormatVersion"/>).</summary>
    private const int Layout = 1;

    private const int BufferSize = 1 << 16;

    private static readonly byte[] Magic = "CLIOFIDX"u8.ToArray();
    private static readonly byte[] EndMarker = "END."u8.ToArray();

    /// <summary>Writes <paramref name="index"/> to <paramref name="stream"/>, which must be seekable and positioned at its start.</summary>
    public static void Write(Stream stream, string manifestId, int catalogEntryCount, ResourceTypeIndex index)
    {
        using var buffered = new BufferedStream(stream, BufferSize);
        using var binary = new BinaryWriter(buffered, Encoding.UTF8, leaveOpen: true);
        binary.Write(Magic);
        binary.Write(Layout);
        binary.Write(manifestId);
        binary.Write(index.ResourceType);
        binary.Write(catalogEntryCount);
        binary.Write(index.BuiltAt.Ticks);
        binary.Write((short)index.BuiltAt.Offset.TotalMinutes);

        var header = new SearchValueBinaryWriter(binary);
        binary.Write7BitEncodedInt(index.Diagnostics.Count);
        foreach (CatalogDiagnostic diagnostic in index.Diagnostics)
        {
            header.WriteString(diagnostic.Code);
            header.WriteString(diagnostic.FilePath);
            binary.Write(diagnostic.Line ?? -1);
            header.WriteString(diagnostic.Message);
        }

        List<(long Offset, int Count)> segments = [];
        for (int start = 0; start < index.Resources.Count; start += SegmentSize)
        {
            binary.Flush();
            int count = Math.Min(SegmentSize, index.Resources.Count - start);
            segments.Add((buffered.Position, count));
            var writer = new SearchValueBinaryWriter(binary);
            for (int position = start; position < start + count; position++)
            {
                WriteResource(binary, writer, index.Resources[position]);
            }
        }

        binary.Flush();
        long table = buffered.Position;
        binary.Write7BitEncodedInt(segments.Count);
        foreach ((long offset, int count) in segments)
        {
            binary.Write(offset);
            binary.Write(count);
        }

        binary.Write(table);
        binary.Write(EndMarker);
    }

    /// <summary>
    /// Reads the type index in <paramref name="stream"/>, re-pointing every entry at the session's own parameter definitions and
    /// every resource at its catalog entry; null when the file was written under another manifest than <paramref name="manifestId"/>
    /// (not corrupt, just not this cache's).
    /// </summary>
    /// <exception cref="InvalidDataException">The file does not match the catalog or the definitions, or is not what the writer wrote.</exception>
    public static ResourceTypeIndex? Read(
        FileStream stream,
        string manifestId,
        string resourceType,
        IReadOnlyList<CatalogEntry> entries,
        ISearchParameterDefinitionManager definitions,
        Stopwatch stopwatch)
    {
        long length = stream.Length;
        if (length < Magic.Length + sizeof(int) + EndMarker.Length + sizeof(long))
        {
            throw new InvalidDataException($"the file is {length} bytes, too short for a cli-on-fhir type index.");
        }

        using MemoryMappedFile map = MemoryMappedFile.CreateFromFile(stream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
        using BinaryReader header = Open(map, 0, length);
        if (!header.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("the file is not a cli-on-fhir type index.");
        }

        int layout = header.ReadInt32();
        if (layout != Layout)
        {
            throw new InvalidDataException($"the file has layout {layout}, not {Layout}.");
        }

        if (!string.Equals(header.ReadString(), manifestId, StringComparison.Ordinal))
        {
            return null;
        }

        string indexedType = header.ReadString();
        if (!string.Equals(indexedType, resourceType, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"the file holds a {indexedType} index.");
        }

        int catalogEntryCount = header.ReadInt32();
        if (catalogEntryCount != entries.Count)
        {
            throw new InvalidDataException($"it was written for {catalogEntryCount} catalogued resources but the folder has {entries.Count}.");
        }

        long builtAtTicks = header.ReadInt64();
        var builtAt = new DateTimeOffset(builtAtTicks, TimeSpan.FromMinutes(header.ReadInt16()));
        List<CatalogDiagnostic> diagnostics = ReadDiagnostics(header);
        IReadOnlyList<(long Offset, int Count)> segments = ReadSegmentTable(map, length);

        Dictionary<string, CatalogEntry> entriesById = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var parts = new List<IndexedResource>[segments.Count];
        try
        {
            Parallel.For(0, segments.Count, segment => parts[segment] = ReadSegment(map, segments, segment, length, resourceType, entriesById, definitions));
        }
        catch (AggregateException ex)
        {
            // A segment's failure is the file's; the caller tells a corrupt file by the exception's type.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }


        List<IndexedResource> resources = new(segments.Sum(segment => segment.Count));
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (List<IndexedResource> part in parts)
        {
            foreach (IndexedResource resource in part)
            {
                if (!seen.Add(resource.Key.Id))
                {
                    throw new InvalidDataException($"{FhirIds.Reference(resourceType, resource.Key.Id)} appears twice.");
                }

                resources.Add(resource);
            }
        }

        return new ResourceTypeIndex(resourceType, resources, diagnostics, builtAt, stopwatch.ElapsedMilliseconds, FromCache: true);
    }

    private static BinaryReader Open(MemoryMappedFile map, long offset, long size) =>
        new(new BufferedStream(map.CreateViewStream(offset, size, MemoryMappedFileAccess.Read), BufferSize), Encoding.UTF8, leaveOpen: false);

    private static void WriteResource(BinaryWriter binary, SearchValueBinaryWriter writer, IndexedResource resource)
    {
        writer.WriteString(resource.Key.Id);
        binary.Write7BitEncodedInt(resource.Index.Count);
        foreach (SearchIndexEntry entry in resource.Index)
        {
            writer.WriteString(entry.SearchParameter.Code);
            writer.WriteValue(entry.Value);
        }

        // Each member is written only when it holds something, as the JSON form did, so most resources cost three bytes here.
        ReferenceResolution resolution = resource.BundleReferences;
        binary.Write7BitEncodedInt(resolution.Resolved.Count);
        foreach ((string written, string target) in resolution.Resolved)
        {
            writer.WriteString(written);
            writer.WriteString(target);
        }

        binary.Write7BitEncodedInt(resolution.UnresolvedUrns.Count);
        foreach (string urn in resolution.UnresolvedUrns)
        {
            writer.WriteString(urn);
        }

        binary.Write7BitEncodedInt(resolution.UnresolvedConditionals.Count);
        foreach (UnresolvedConditional conditional in resolution.UnresolvedConditionals)
        {
            writer.WriteString(conditional.Reference);
            writer.WriteString(conditional.Failure.Code);
        }
    }

    private static List<CatalogDiagnostic> ReadDiagnostics(BinaryReader binary)
    {
        var reader = new SearchValueBinaryReader(binary);
        int count = ReadCount(binary);
        List<CatalogDiagnostic> diagnostics = new(count);
        for (int index = 0; index < count; index++)
        {
            string code = reader.ReadRequiredString("A diagnostic's code");
            string filePath = reader.ReadRequiredString("A diagnostic's file");
            int line = binary.ReadInt32();
            diagnostics.Add(new CatalogDiagnostic(code, filePath, line < 0 ? null : line, reader.ReadRequiredString("A diagnostic's message")));
        }

        return diagnostics;
    }

    private static long TableOffset(MemoryMappedFile map, long length)
    {
        using BinaryReader trailer = Open(map, length - EndMarker.Length - sizeof(long), EndMarker.Length + sizeof(long));
        long table = trailer.ReadInt64();
        if (!trailer.ReadBytes(EndMarker.Length).AsSpan().SequenceEqual(EndMarker))
        {
            throw new InvalidDataException("the file does not end with the end marker.");
        }

        return table >= 0 && table <= length - EndMarker.Length - sizeof(long) ? table : throw new InvalidDataException($"the segment table offset {table} is outside the file.");
    }

    private static List<(long Offset, int Count)> ReadSegmentTable(MemoryMappedFile map, long length)
    {
        long table = TableOffset(map, length);
        long size = length - EndMarker.Length - sizeof(long) - table;
        using BinaryReader reader = Open(map, table, size);
        int count = ReadCount(reader);
        List<(long Offset, int Count)> segments = new(count);
        long previous = -1;
        for (int index = 0; index < count; index++)
        {
            long offset = reader.ReadInt64();
            int resources = reader.ReadInt32();
            if (offset <= previous || offset > table || resources <= 0 || resources > SegmentSize)
            {
                throw new InvalidDataException($"segment {index} is at {offset} with {resources} resources, which the file cannot hold.");
            }

            previous = offset;
            segments.Add((offset, resources));
        }

        return reader.BaseStream.Position == size ? segments : throw new InvalidDataException("the segment table does not end where the trailer starts.");
    }

    private static List<IndexedResource> ReadSegment(
        MemoryMappedFile map,
        IReadOnlyList<(long Offset, int Count)> segments,
        int segment,
        long length,
        string resourceType,
        Dictionary<string, CatalogEntry> entriesById,
        ISearchParameterDefinitionManager definitions)
    {
        (long offset, int count) = segments[segment];
        long end = segment + 1 < segments.Count ? segments[segment + 1].Offset : TableOffset(map, length);
        using BinaryReader binary = Open(map, offset, end - offset);
        List<IndexedResource> resources = ReadResources(binary, count, resourceType, entriesById, definitions);
        return binary.BaseStream.Position == end - offset
            ? resources
            : throw new InvalidDataException($"segment {segment} does not end where the next part of the file starts.");
    }

    private static List<IndexedResource> ReadResources(
        BinaryReader binary,
        int count,
        string resourceType,
        Dictionary<string, CatalogEntry> entriesById,
        ISearchParameterDefinitionManager definitions)
    {
        var reader = new SearchValueBinaryReader(binary);
        Dictionary<string, SearchParameterInfo> parametersByCode = new(StringComparer.Ordinal);
        List<IndexedResource> resources = new(count);
        for (int index = 0; index < count; index++)
        {
            string id = reader.ReadRequiredString("A resource's id");
            if (!entriesById.TryGetValue(id, out CatalogEntry? entry))
            {
                throw new InvalidDataException($"{FhirIds.Reference(resourceType, id)} is not in the folder's catalog.");
            }

            int entryCount = ReadCount(binary);
            List<SearchIndexEntry> indexEntries = new(entryCount);
            for (int position = 0; position < entryCount; position++)
            {
                SearchParameterInfo parameter = Parameter(reader.ReadRequiredString("An entry's parameter"), resourceType, definitions, parametersByCode);
                indexEntries.Add(new SearchIndexEntry(parameter, reader.ReadValue()));
            }

            resources.Add(new IndexedResource(new ResourceKey(resourceType, id), entry, indexEntries, ReadResolution(binary, reader)));
        }

        return resources;
    }

    // Entries persist only the parameter's code; searching needs the real definition, so every entry is re-pointed at the
    // session's own and an unknown code invalidates the file.
    private static SearchParameterInfo Parameter(string code, string resourceType, ISearchParameterDefinitionManager definitions, Dictionary<string, SearchParameterInfo> parametersByCode)
    {
        if (!parametersByCode.TryGetValue(code, out SearchParameterInfo? parameter))
        {
            if (!definitions.TryGetSearchParameter(resourceType, code, out parameter))
            {
                throw new InvalidDataException($"search parameter '{code}' is not defined for {resourceType}.");
            }

            parametersByCode.Add(code, parameter);
        }

        return parameter;
    }

    private static ReferenceResolution ReadResolution(BinaryReader binary, SearchValueBinaryReader reader)
    {
        int resolvedCount = ReadCount(binary);
        Dictionary<string, string>? resolved = resolvedCount == 0 ? null : new(resolvedCount, StringComparer.Ordinal);
        for (int index = 0; index < resolvedCount; index++)
        {
            string written = reader.ReadRequiredString("A resolved reference");
            if (!resolved!.TryAdd(written, reader.ReadRequiredString("A resolved reference's target")))
            {
                throw new InvalidDataException($"the resolved reference '{written}' appears twice.");
            }
        }

        int urnCount = ReadCount(binary);
        List<string> urns = new(urnCount);
        for (int index = 0; index < urnCount; index++)
        {
            urns.Add(reader.ReadRequiredString("An unresolved urn"));
        }

        int conditionalCount = ReadCount(binary);
        List<UnresolvedConditional> conditionals = new(conditionalCount);
        for (int index = 0; index < conditionalCount; index++)
        {
            string reference = reader.ReadRequiredString("An unresolved conditional reference");
            string code = reader.ReadRequiredString("A conditional failure");
            conditionals.Add(new UnresolvedConditional(
                reference,
                ConditionalReferenceFailure.All.FirstOrDefault(failure => failure.Code == code) ?? throw new InvalidDataException($"'{code}' is not a conditional reference failure code.")));
        }

        return resolved is null && urns.Count == 0 && conditionals.Count == 0
            ? ReferenceResolution.None
            : new ReferenceResolution(resolved ?? new Dictionary<string, string>(StringComparer.Ordinal), urns, conditionals);
    }

    private static int ReadCount(BinaryReader binary) => SearchValueBinaryReader.ReadCount(binary);
}
