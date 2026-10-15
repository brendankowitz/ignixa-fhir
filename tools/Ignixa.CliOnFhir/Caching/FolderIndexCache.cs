// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.CliOnFhir.Caching;

/// <summary>
/// Persists each resource type's index under a per-folder directory outside the source folder, and loads it
/// back when nothing it depends on has changed. A cache that cannot be used is reported and bypassed, never
/// partially served: the caller then builds the index as if there were no cache.
/// </summary>
/// <remarks>
/// The first use compares the stored manifest with the current one. A match keeps the stored type files; any
/// mismatch replaces the manifest, which orphans the old type files (each names the manifest it was written
/// under), so a type file is only ever read together with the manifest that vouched for it. All writes go to
/// a temporary file that is then moved into place, so concurrent processes can only replace each other's
/// complete files. Thread-safe.
/// </remarks>
internal sealed class FolderIndexCache
{
    public const string RebuiltCode = "index-cache-rebuilt";
    public const string CorruptCode = "index-cache-corrupt";
    public const string UnavailableCode = "index-cache-unavailable";
    public const string UnsupportedValueCode = "index-cache-unsupported-value";

    private const string ManifestFileName = "manifest.json";
    private const string TypeFileSuffix = ".index.json";
    private const int FileBufferSize = 1 << 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IndexCacheOptions _options;
    private readonly FolderCatalog _catalog;
    private readonly string _folderPath;
    private readonly string _fhirVersion;
    private readonly ISearchParameterDefinitionManager _definitions;
    private readonly string _canonicalFolderPath;
    private readonly string? _unavailableReason;
    private readonly Lock _gate = new();
    private readonly List<IssueItem> _issues = [];
    private bool _opened;
    private string? _manifestId;
    private bool _writesDisabled;
    private bool _unavailableReported;

    public FolderIndexCache(IndexCacheOptions options, FolderCatalog catalog, string fhirVersion, ISearchParameterDefinitionManager definitions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(fhirVersion);
        ArgumentNullException.ThrowIfNull(definitions);

        _options = options;
        _catalog = catalog;
        _fhirVersion = fhirVersion;
        _definitions = definitions;
        _folderPath = NormalizeFolderPath(catalog.RootPath);
        _canonicalFolderPath = ResolveOrNull(catalog.RootPath) ?? Path.GetFullPath(catalog.RootPath);
        _unavailableReason = options.UnavailableReason ?? (options.RootDirectory is null ? null : OverlapReason(options.RootDirectory));
        CacheDirectory = options.RootDirectory is null || _unavailableReason is not null
            ? null
            : Path.Combine(options.RootDirectory, FolderDirectoryName(_folderPath, fhirVersion));
    }

    /// <summary>This folder's cache directory; null when the cache is not in use.</summary>
    public string? CacheDirectory { get; }

    /// <summary>Problems met while using the cache, in the order they occurred.</summary>
    public IReadOnlyList<IssueItem> Issues
    {
        get
        {
            lock (_gate)
            {
                return [.. _issues];
            }
        }
    }

    /// <summary>
    /// Loads the persisted index of <paramref name="resourceType"/>, or returns null when there is none to use:
    /// the cache is off, nothing was stored for the type, or the stored file is unreadable (reported as an issue).
    /// </summary>
    public ResourceTypeIndex? TryLoad(string resourceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        string? manifestId = OpenManifest();
        if (manifestId is null)
        {
            return null;
        }

        string path = TypeFilePath(resourceType);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            CachedTypeIndex? cached;
            using (FileStream stream = OpenShared(path, FileOptions.SequentialScan))
            {
                try
                {
                    cached = JsonSerializer.Deserialize<CachedTypeIndex>(stream, JsonOptions);
                }
                catch (Exception ex) when (IsUnreadableCacheFile(ex) && WrittenUnderAnotherManifest(path, manifestId))
                {
                    // A file of an older shape from an older manifest is simply not ours.
                    return null;
                }
            }

            if (cached?.ManifestId is not { } writtenUnder)
            {
                throw new InvalidDataException("the file names no manifest.");
            }

            // Written under a different manifest (an older run, or a concurrent one): not corrupt, just not ours.
            if (!string.Equals(writtenUnder, manifestId, StringComparison.Ordinal))
            {
                return null;
            }

            cached.Validate();
            return Materialize(cached, resourceType, stopwatch);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (IsUnreadableCacheFile(ex))
        {
            AddIssue("warning", CorruptCode, $"The cached {resourceType} index at '{path}' could not be used and was rebuilt: {Describe(ex)}");
            return null;
        }
    }

    /// <summary>
    /// Persists <paramref name="index"/>. Returns the time the write took, or null when it was not persisted
    /// (cache off or unavailable, in which case an issue says why).
    /// </summary>
    public long? Store(ResourceTypeIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        string? manifestId = OpenManifest();
        if (manifestId is null || WritesDisabled())
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var cached = new CachedTypeIndex(
            manifestId,
            index.ResourceType,
            EntriesOf(index.ResourceType).Length,
            index.BuiltAt,
            index.Diagnostics,
            [.. index.Resources.Select(resource => new CachedResource(
                resource.Key.Id,
                [.. resource.Index.Select(entry => new CachedEntry(entry.SearchParameter.Code, entry.Value))]))]);

        // A value type the format does not know throws here (a programmer error); a value it knows but cannot
        // reproduce exactly is reported and the type is left uncached, so a reload can never differ from a build.
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(cached, JsonOptions);
        if (FindUnsupportedValue(cached, bytes) is { } unsupported)
        {
            AddIssue("warning", UnsupportedValueCode, $"The {index.ResourceType} index was not saved to the cache because {unsupported}");
            return null;
        }

        try
        {
            WriteAtomically(TypeFilePath(index.ResourceType), stream => stream.Write(bytes));
            return stopwatch.ElapsedMilliseconds;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportUnavailable($"The index cache at '{CacheDirectory}' cannot be written: {Describe(ex)}");
            return null;
        }
    }

    /// <summary>
    /// Reads <paramref name="bytes"/> back the way a later run would and checks that writing the result again gives the
    /// same bytes, which proves every persisted field reproduces. Returns why not, naming the first offending value.
    /// </summary>
    private static string? FindUnsupportedValue(CachedTypeIndex cached, byte[] bytes)
    {
        if (RoundTripsTo(bytes, out _))
        {
            return null;
        }

        foreach (CachedResource resource in cached.Resources)
        {
            foreach (CachedEntry entry in resource.Entries)
            {
                byte[] single = JsonSerializer.SerializeToUtf8Bytes(new[] { entry }, JsonOptions);
                if (!RoundTripsTo<CachedEntry[]>(single, out string? problem))
                {
                    return $"the '{entry.Code}' value of {cached.IndexedType}/{resource.Id} does not survive it ({problem}).";
                }
            }
        }

        return "the file does not read back identically.";
    }

    private static bool RoundTripsTo(byte[] bytes, out string? problem) => RoundTripsTo<CachedTypeIndex>(bytes, out problem);

    private static bool RoundTripsTo<T>(byte[] bytes, out string? problem)
        where T : class
    {
        try
        {
            T? reloaded = JsonSerializer.Deserialize<T>(bytes, JsonOptions);
            if (reloaded is CachedTypeIndex typeIndex)
            {
                typeIndex.Validate();
            }

            bool identical = reloaded is not null && JsonSerializer.SerializeToUtf8Bytes(reloaded, JsonOptions).AsSpan().SequenceEqual(bytes);
            problem = identical ? null : "it reads back different";
            return identical;
        }
        catch (Exception ex) when (IsUnreadableCacheFile(ex))
        {
            problem = Describe(ex);
            return false;
        }
    }

    private static string NormalizeFolderPath(string path)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? trimmed.ToUpperInvariant() : trimmed;
    }

    private static string FolderDirectoryName(string folderPath, string fhirVersion)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folderPath)))[..16];
        return $"{hash}-{fhirVersion}";
    }

    private static bool IsUnreadableCacheFile(Exception ex) =>
        ex is JsonException or InvalidDataException or ArgumentException or FormatException or InvalidOperationException
            or NotSupportedException or OverflowException or IOException or UnauthorizedAccessException;

    // Reads only as far as needed to tell whether a file that no longer deserializes belongs to another manifest.
    private static bool WrittenUnderAnotherManifest(string path, string manifestId)
    {
        try
        {
            using FileStream stream = OpenShared(path, FileOptions.None);
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("manifestId", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
                && !string.Equals(id.GetString(), manifestId, StringComparison.Ordinal);
        }
        catch (Exception ex) when (IsUnreadableCacheFile(ex))
        {
            return false;
        }
    }

    // Delete sharing lets another process replace the file (File.Move over it) while this one is still reading.
    private static FileStream OpenShared(string path, FileOptions options) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, FileBufferSize, options);

    /// <summary>
    /// The cache must neither sit inside the data folder (the folder stays read-only, and cache files would be
    /// catalogued as data) nor contain it (another run's cache would then be inside the folder it indexes). Both
    /// paths are compared with links resolved, so a symlinked cache directory cannot lead back into the folder, and
    /// ignoring case, because a false refusal only costs the cache while a false pass writes into the data.
    /// </summary>
    private string? OverlapReason(string cachePath)
    {
        string cache;
        try
        {
            cache = CanonicalPath.Resolve(cachePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"the cache directory '{cachePath}' cannot be checked against the data folder ({Describe(ex)})";
        }

        if (IsSameOrUnder(cache, _canonicalFolderPath))
        {
            return $"the cache directory '{cache}' is inside the data folder '{_canonicalFolderPath}'; the folder is never written and cache files would be read as data. Use --cache-dir outside the folder";
        }

        return IsSameOrUnder(_canonicalFolderPath, cache)
            ? $"the data folder '{_canonicalFolderPath}' is inside the cache directory '{cache}'; cache files would be read as data. Use --cache-dir outside the folder"
            : null;
    }

    private static string? ResolveOrNull(string path)
    {
        try
        {
            return CanonicalPath.Resolve(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsSameOrUnder(string path, string parent)
    {
        string child = Path.TrimEndingDirectorySeparator(path);
        string root = Path.TrimEndingDirectorySeparator(parent);
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(child, root, StringComparison.OrdinalIgnoreCase) || child.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    private void WriteAtomically(string path, Action<Stream> write)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        // Checked again now that the directory exists: a link that was not there (or not followed) before
        // would otherwise send the write into the data folder.
        if (OverlapReason(directory) is { } overlap)
        {
            throw new IOException($"Refusing to write the index cache: {overlap}.");
        }

        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileBufferSize))
            {
                write(stream);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    private CatalogEntry[] EntriesOf(string resourceType) =>
        [.. _catalog.Entries.Where(entry => string.Equals(entry.ResourceType, resourceType, StringComparison.Ordinal))];

    private string TypeFilePath(string resourceType) => Path.Combine(CacheDirectory!, resourceType + TypeFileSuffix);

    private bool WritesDisabled()
    {
        lock (_gate)
        {
            return _writesDisabled;
        }
    }

    private void AddIssue(string severity, string code, string message)
    {
        lock (_gate)
        {
            _issues.Add(new IssueItem(severity, code, message, Location: null));
        }
    }

    private void ReportUnavailable(string message)
    {
        lock (_gate)
        {
            _writesDisabled = true;
            if (!_unavailableReported)
            {
                _unavailableReported = true;
                _issues.Add(new IssueItem("warning", UnavailableCode, message + " Continuing without the cache.", Location: null));
            }
        }
    }

    /// <summary>
    /// Checks the stored manifest once per session. Returns the id of the manifest that type files may be read
    /// and written under, or null when the cache cannot be used at all.
    /// </summary>
    private string? OpenManifest()
    {
        lock (_gate)
        {
            if (!_opened)
            {
                _opened = true;
                _manifestId = ValidateOrReplaceManifest();
            }

            return _manifestId;
        }
    }

    private string? ValidateOrReplaceManifest()
    {
        if (CacheDirectory is null)
        {
            if (_unavailableReason is { } reason)
            {
                ReportUnavailable($"The index cache is not available: {reason}.");
            }

            return null;
        }

        // The per-folder directory may itself be a link planted under a perfectly good root.
        if (OverlapReason(CacheDirectory) is { } overlap)
        {
            ReportUnavailable($"The index cache is not available: {overlap}.");
            return null;
        }

        CacheManifest current = CacheManifest.For(_catalog, _folderPath, _fhirVersion, _definitions);
        string manifestPath = Path.Combine(CacheDirectory, ManifestFileName);
        byte[]? stored = ReadStoredManifest(manifestPath);
        if (stored is not null && TryMatch(stored, current, manifestPath))
        {
            return IdOf(stored);
        }

        byte[] replacement = JsonSerializer.SerializeToUtf8Bytes(current, JsonOptions);
        try
        {
            WriteAtomically(manifestPath, stream => stream.Write(replacement));
            return IdOf(replacement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportUnavailable($"The index cache at '{CacheDirectory}' cannot be written: {Describe(ex)}");
            return null;
        }
    }

    private byte[]? ReadStoredManifest(string manifestPath)
    {
        try
        {
            using FileStream stream = OpenShared(manifestPath, FileOptions.None);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddIssue("warning", CorruptCode, $"The cache manifest at '{manifestPath}' could not be read and the cache was rebuilt: {Describe(ex)}");
            return null;
        }
    }

    private bool TryMatch(byte[] stored, CacheManifest current, string manifestPath)
    {
        string? differentCache;
        ManifestMismatch? changedFiles = null;
        try
        {
            // Identity first, straight from the JSON: a manifest from another format, tool or definitions is a
            // different cache, not a damaged one, even if its shape no longer deserializes.
            using JsonDocument document = JsonDocument.Parse(stored);
            differentCache = CacheManifest.CompareIdentity(document.RootElement, current);
            if (differentCache is null)
            {
                CacheManifest manifest = document.RootElement.Deserialize<CacheManifest>(JsonOptions)
                    ?? throw new InvalidDataException("the manifest holds a JSON null.");
                manifest.Validate();
                changedFiles = manifest.CompareFiles(current);
                differentCache = changedFiles?.Reason;
            }
        }
        catch (Exception ex) when (IsUnreadableCacheFile(ex))
        {
            AddIssue("warning", CorruptCode, $"The cache manifest at '{manifestPath}' could not be used and the cache was rebuilt: {Describe(ex)}");
            return false;
        }

        if (differentCache is not null)
        {
            AddIssue("information", RebuiltCode, $"The cached index was discarded and is being rebuilt: {differentCache}.");
        }

        return differentCache is null;
    }

    private static string IdOf(byte[] manifestBytes) => Convert.ToHexStringLower(SHA256.HashData(manifestBytes));

    private ResourceTypeIndex Materialize(CachedTypeIndex cached, string resourceType, Stopwatch stopwatch)
    {
        if (!string.Equals(cached.IndexedType, resourceType, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"the file holds a {cached.IndexedType} index.");
        }

        CatalogEntry[] entries = EntriesOf(resourceType);
        if (cached.CatalogEntryCount != entries.Length)
        {
            throw new InvalidDataException($"it was written for {cached.CatalogEntryCount} catalogued resources but the folder has {entries.Length}.");
        }

        Dictionary<string, CatalogEntry> entriesById = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        Dictionary<string, SearchParameterInfo> parametersByCode = new(StringComparer.Ordinal);
        List<IndexedResource> resources = new(cached.Resources.Count);
        foreach (CachedResource resource in cached.Resources)
        {
            if (!entriesById.TryGetValue(resource.Id, out CatalogEntry? entry))
            {
                throw new InvalidDataException($"{resourceType}/{resource.Id} is not in the folder's catalog.");
            }

            resources.Add(new IndexedResource(new ResourceKey(resourceType, resource.Id), entry, Rebind(resource, resourceType, parametersByCode)));
        }

        return new ResourceTypeIndex(resourceType, resources, cached.Diagnostics, cached.BuiltAt, stopwatch.ElapsedMilliseconds, FromCache: true);
    }

    // Entries persist only the parameter's code; searching needs the real definition, so every entry is re-pointed
    // at the session's own and an unknown code invalidates the file.
    private List<SearchIndexEntry> Rebind(CachedResource resource, string resourceType, Dictionary<string, SearchParameterInfo> parametersByCode)
    {
        List<SearchIndexEntry> bound = new(resource.Entries.Count);
        foreach (CachedEntry entry in resource.Entries)
        {
            string code = entry.Code;
            if (!parametersByCode.TryGetValue(code, out SearchParameterInfo? parameter))
            {
                if (!_definitions.TryGetSearchParameter(resourceType, code, out parameter))
                {
                    throw new InvalidDataException($"search parameter '{code}' is not defined for {resourceType}.");
                }

                parametersByCode.Add(code, parameter);
            }

            bound.Add(new SearchIndexEntry(parameter, entry.Value));
        }

        return bound;
    }
}
