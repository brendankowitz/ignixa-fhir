// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text.Json;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Npm;

/// <summary>
/// The tool's package store, <c>&lt;cache root&gt;/packages</c>: each downloaded package as <c>&lt;id&gt;#&lt;version&gt;.tgz</c>
/// with a <c>.json</c> sidecar (<see cref="StoredPackageRecord"/>). A package is stored only after it was verified and read,
/// and is re-verified against its recorded SHA-512 on every read; a mismatch is reported, never read past or replaced
/// silently. Writes go to a temporary file moved into place, never into the data folder.
/// </summary>
public sealed class PackageStore
{
    public const string CorruptCode = "package-store-corrupt";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public PackageStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory = Path.GetFullPath(directory);
    }

    public string Directory { get; }

    /// <summary>The store directory for the cache root <paramref name="cacheRoot"/>.</summary>
    public static PackageStore Under(string cacheRoot) => new(Path.Combine(cacheRoot, "packages"));

    public string ArchivePath(string id, string version) => Path.Combine(Directory, $"{id}#{version}.tgz");

    /// <summary>The stored archive of <paramref name="id"/>@<paramref name="version"/>, verified; null when not stored.</summary>
    /// <exception cref="ToolRequestException">The stored archive or its sidecar is unreadable or does not match its digest.</exception>
    public byte[]? TryRead(string id, string version)
    {
        string archivePath = ArchivePath(id, version);
        string recordPath = Path.ChangeExtension(archivePath, ".json");
        if (!File.Exists(recordPath))
        {
            return null;
        }

        try
        {
            StoredPackageRecord record = JsonSerializer.Deserialize<StoredPackageRecord>(File.ReadAllBytes(recordPath), JsonOptions)
                ?? throw new InvalidDataException("empty sidecar");
            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > PackageLimits.MaxCompressedBytes)
            {
                throw new InvalidDataException("archive over the size limit");
            }

            byte[] archive = new byte[stream.Length];
            stream.ReadExactly(archive);
            if (!string.Equals(Convert.ToHexStringLower(SHA512.HashData(archive)), record.Sha512, StringComparison.Ordinal)
                || record.Id != id || record.Version != version)
            {
                throw new InvalidDataException("the archive does not match the SHA-512 recorded when it was stored");
            }

            return archive;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException)
        {
            throw ToolRequestException.Error(CorruptCode, $"The stored package {id}@{version} in '{Directory}' cannot be used ({ex.GetType().Name}: {ex.Message}). Delete '{archivePath}' and its .json sidecar to download it again.");
        }
    }

    /// <summary>The versions of <paramref name="id"/> the store holds, in ordinal order.</summary>
    public IReadOnlyList<string> Versions(string id)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        string prefix = id + "#";
        return [.. System.IO.Directory.EnumerateFiles(Directory, prefix + "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path)[prefix.Length..])
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Stores a verified archive. The overlap check runs again after the directory exists, so a link created meanwhile cannot redirect the write into the data folder.</summary>
    /// <exception cref="IOException">The store cannot be written, or would be inside the data folder.</exception>
    internal void Save(StoredPackageRecord record, byte[] archive, FolderOverlap overlap)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(overlap);

        if (overlap.Reason(Directory, "package store") is { } before)
        {
            throw new IOException($"Refusing to write the package store: {before}.");
        }

        System.IO.Directory.CreateDirectory(Directory);
        if (overlap.Reason(Directory, "package store") is { } after)
        {
            throw new IOException($"Refusing to write the package store: {after}.");
        }

        string archivePath = ArchivePath(record.Id, record.Version);
        WriteAtomically(archivePath, archive);
        WriteAtomically(Path.ChangeExtension(archivePath, ".json"), JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions));
    }

    private static void WriteAtomically(string path, byte[] content)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
