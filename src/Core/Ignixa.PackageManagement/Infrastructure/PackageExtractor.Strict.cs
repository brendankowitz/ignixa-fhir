using System.Formats.Tar;
using System.Text;
using System.Text.Json;
using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Infrastructure;

public partial class PackageExtractor
{
    /// <summary>
    /// Validates and extracts one bounded .tgz from the current position through EOF.
    /// Leaves the caller's stream open. Cancellation is propagated, not converted to a diagnostic.
    /// </summary>
    /// <param name="packageStream">Caller-owned readable input; seeking is not required.</param>
    /// <param name="limits">Inclusive positive limits, including ignored bodies and hidden tar metadata.</param>
    /// <param name="cancellationToken">Forwarded to pending input reads and checked between parsing operations.</param>
    /// <returns>A complete in-memory result with read-only entry and FHIR-version collections.</returns>
    /// <exception cref="ArgumentNullException">Input or limits is null.</exception>
    /// <exception cref="ArgumentException">Input is not readable.</exception>
    /// <exception cref="PackageExtractionException">Archive, path, manifest, JSON or limit validation fails.</exception>
    /// <exception cref="OperationCanceledException">Cancellation is observed.</exception>
    /// <exception cref="IOException">The caller's input fails; its original exception propagates.</exception>
    /// <remarks>
    /// Byte limits are not peak-memory limits. CPU parsing is cooperatively cancellable, not
    /// preemptible inside synchronous library calls. Pending reads rely on the input honoring cancellation.
    /// No result is returned until physical framing and the reader's complete traversal agree.
    /// </remarks>
    public async Task<StrictPackageExtractionResult> ExtractStrictAsync(
        Stream packageStream,
        PackageExtractionLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        ArgumentNullException.ThrowIfNull(limits);
        if (!packageStream.CanRead)
        {
            throw new ArgumentException("Package stream must be readable.", nameof(packageStream));
        }
        cancellationToken.ThrowIfCancellationRequested();
        byte[] compressed = await StrictGzipArchive.ReadInputAsync(packageStream, limits.MaxCompressedBytes, cancellationToken);
        byte[] expanded = StrictGzipArchive.Expand(compressed, limits.MaxExpandedBytes, cancellationToken);
        (int physicalEntries, long payloadBytes, List<StrictTarFrame> frames) =
            StrictTarFraming.Validate(expanded, limits, cancellationToken);

        StrictPackageManifest? manifest = null;
        var jsonEntries = new List<StrictPackageEntry>();
        var paths = new Dictionary<string, (bool Directory, int Index)>(StringComparer.OrdinalIgnoreCase);
        int logicalEntries = 0;
        int physicalIndex = 1;
        using var tarStream = new MemoryStream(expanded, writable: false);
        using var reader = new TarReader(tarStream);
        try
        {
            foreach (StrictTarFrame frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                physicalIndex = frame.PhysicalIndex;
                bool directory = frame.Directory;
                string path = ValidateStrictPath(frame.Path, directory, physicalIndex);
                TarEntry? entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken);
                // Reconcile every logical entry with its physical body, not just the final count.
                // This also covers ignored files and early reader termination after a manifest.
                if (entry is null || entry.DataOffset != frame.DataOffset || entry.Length != frame.Size ||
                    !string.Equals(entry.Name, frame.Path, StringComparison.Ordinal) ||
                    (directory ? entry.EntryType != TarEntryType.Directory :
                        entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)))
                {
                    throw StrictFailure(PackageExtractionError.InvalidArchive, physicalIndex);
                }
                logicalEntries++;
                bool isManifest = path == "package/package.json";
                if (isManifest && (manifest is not null || directory))
                {
                    throw StrictFailure(directory ? PackageExtractionError.InvalidManifest : PackageExtractionError.DuplicateManifest, physicalIndex);
                }
                if (!paths.TryAdd(path, (directory, physicalIndex)))
                {
                    throw StrictFailure(PackageExtractionError.DuplicatePath, physicalIndex);
                }
                if (directory || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                byte[] content = await ReadStrictEntryAsync(entry, limits.MaxEntryBytes, physicalIndex, cancellationToken);
                PackageExtractionError jsonError = isManifest ? PackageExtractionError.InvalidManifest : PackageExtractionError.InvalidJson;
                using JsonDocument document = ParseStrictJson(content, limits.MaxJsonDepth, physicalIndex, jsonError, cancellationToken);
                string json = Encoding.UTF8.GetString(content);
                if (isManifest)
                {
                    manifest = ParseStrictManifest(document.RootElement, json, physicalIndex);
                }
                else
                {
                    string? resourceType = null;
                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty("resourceType", out JsonElement type))
                    {
                        resourceType = ReadNonemptyString(type, jsonError, physicalIndex);
                    }
                    jsonEntries.Add(new(path, json, resourceType));
                }
            }
            physicalIndex = physicalEntries + 1;
            if (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is not null)
            {
                throw StrictFailure(PackageExtractionError.InvalidArchive, physicalIndex);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or
            FormatException or OverflowException or ArgumentException)
        {
            throw StrictFailure(PackageExtractionError.InvalidArchive, physicalIndex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ValidateStrictPathCollisions(paths, cancellationToken);
        if (manifest is null)
        {
            throw StrictFailure(PackageExtractionError.MissingManifest);
        }
        return new(manifest, jsonEntries.AsReadOnly(),
            new(compressed.Length, expanded.Length, payloadBytes, physicalEntries, logicalEntries));
    }

    private static string ValidateStrictPath(string path, bool directory, int entry)
    {
        string normalized = directory && path.EndsWith('/') ? path[..^1] : path;
        string[] segments = normalized.Split('/');
        if (path.Length == 0 || path.Contains('\\', StringComparison.Ordinal) || path.Any(c => char.IsControl(c) || c is ':' or '\uFFFD') ||
            segments.Any(s => s.Length == 0 || s is "." or ".." || s.EndsWith('.') || s.EndsWith(' ')))
        {
            throw StrictFailure(PackageExtractionError.UnsafePath, entry);
        }
        if (segments[^1].Equals("package.json", StringComparison.OrdinalIgnoreCase) &&
            normalized != "package/package.json")
        {
            throw StrictFailure(PackageExtractionError.InvalidManifest, entry);
        }
        if (segments[0] != "package" || (segments.Length == 1 && !directory))
        {
            throw StrictFailure(PackageExtractionError.UnsafePath, entry);
        }
        return normalized;
    }

    private static void ValidateStrictPathCollisions(
        Dictionary<string, (bool Directory, int Index)> paths, CancellationToken cancellationToken)
    {
        // Materializing every ancestor prefix is quadratic for deeply nested GNU/Pax paths.
        // Search each file's descendant range using only the full paths already retained.
        string[] sorted = paths.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string path in sorted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (bool directory, int index) = paths[path];
            if (directory)
            {
                continue;
            }
            string prefix = path + "/";
            int match = Array.BinarySearch(sorted, prefix, StringComparer.OrdinalIgnoreCase);
            int candidate = match >= 0 ? match : ~match;
            if (candidate < sorted.Length && sorted[candidate].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw StrictFailure(PackageExtractionError.DuplicatePath, index);
            }
        }
    }

    private static async Task<byte[]> ReadStrictEntryAsync(
        TarEntry entry, int maximum, int index, CancellationToken cancellationToken)
    {
        if (entry.Length > maximum)
        {
            throw StrictFailure(PackageExtractionError.EntrySizeLimit, index, maximum);
        }
        using var output = new MemoryStream();
        if (entry.DataStream is not null)
        {
            var buffer = new byte[16 * 1024];
            int count;
            while ((count = await entry.DataStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (output.Length + count > maximum)
                {
                    throw StrictFailure(PackageExtractionError.EntrySizeLimit, index, maximum);
                }
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
        }
        if (output.Length != entry.Length)
        {
            throw StrictFailure(PackageExtractionError.InvalidArchive, index);
        }
        return output.ToArray();
    }

    private static JsonDocument ParseStrictJson(
        byte[] content, int maxDepth, int entry, PackageExtractionError error, CancellationToken cancellationToken)
    {
        try
        {
            // Inspect depth explicitly to give a stable limit diagnostic rather than parsing
            // localized JsonException messages. Duplicate properties have no unambiguous identity.
            var reader = new Utf8JsonReader(content, new JsonReaderOptions { MaxDepth = int.MaxValue });
            var objects = new Stack<HashSet<string>>();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject &&
                    reader.CurrentDepth >= maxDepth)
                {
                    throw StrictFailure(PackageExtractionError.JsonDepthLimit, entry, maxDepth);
                }
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        objects.Push(new(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        objects.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        if (!objects.Peek().Add(reader.GetString()!))
                        {
                            throw StrictFailure(error, entry);
                        }
                        break;
                    case JsonTokenType.String:
                        // Tokenization alone does not validate UTF-8 or escaped surrogate pairs.
                        _ = reader.GetString();
                        break;
                }
            }
            return JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = maxDepth });
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw StrictFailure(error, entry);
        }
    }

    private static StrictPackageManifest ParseStrictManifest(JsonElement root, string json, int entry)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("name", out JsonElement name) || !root.TryGetProperty("version", out JsonElement version))
        {
            throw StrictFailure(PackageExtractionError.InvalidManifest, entry);
        }
        string packageName = ReadNonemptyString(name, PackageExtractionError.InvalidManifest, entry);
        string packageVersion = ReadNonemptyString(version, PackageExtractionError.InvalidManifest, entry);
        string? singular = root.TryGetProperty("fhirVersion", out JsonElement fhirVersion)
            ? ReadNonemptyString(fhirVersion, PackageExtractionError.InvalidManifest, entry) : null;
        var plural = new List<string>();
        if (root.TryGetProperty("fhirVersions", out JsonElement fhirVersions))
        {
            if (fhirVersions.ValueKind != JsonValueKind.Array)
            {
                throw StrictFailure(PackageExtractionError.InvalidManifest, entry);
            }
            foreach (JsonElement value in fhirVersions.EnumerateArray())
            {
                plural.Add(ReadNonemptyString(value, PackageExtractionError.InvalidManifest, entry));
            }
        }
        return new(packageName, packageVersion, singular, plural.AsReadOnly(), json);
    }

    private static string ReadNonemptyString(JsonElement value, PackageExtractionError error, int entry)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw StrictFailure(error, entry);
        }
        return value.GetString()!;
    }

    private static PackageExtractionException StrictFailure(PackageExtractionError code, int? entry = null, long? limit = null) =>
        new(new(code, entry, limit));
}
