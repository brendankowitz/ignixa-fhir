// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Formats.Tar;
using System.IO.Compression;

namespace Ignixa.CliOnFhir.Terminology.Npm;

/// <summary>
/// Reads a FHIR npm package <c>.tgz</c> in memory, within <see cref="PackageLimits"/>. Nothing is written to disk, so entry
/// paths are only ever compared, never used as file names. Entries other than regular files, and files outside
/// <c>package/</c> (the core packages ship <c>other/</c> and <c>openapi/</c>), are skipped and counted; a repeated path,
/// a missing or repeated <c>package/package.json</c>, or anything over a limit makes the whole package unreadable.
/// </summary>
internal static class PackageArchiveReader
{
    /// <exception cref="PackageFormatException">The archive is corrupt, over a limit, or has no usable manifest.</exception>
    public static PackageContents Read(byte[] archive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (archive.Length > PackageLimits.MaxCompressedBytes)
        {
            throw new PackageFormatException($"The package archive is larger than {PackageLimits.MaxCompressedBytes} bytes.");
        }

        try
        {
            using var compressed = new MemoryStream(archive, writable: false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var expanded = new BoundedReadStream(gzip, PackageLimits.MaxExpandedBytes);
            using var tar = new TarReader(expanded);
            return ReadEntries(tar, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or FormatException or ArgumentException or InvalidOperationException)
        {
            throw new PackageFormatException($"The package archive is not a readable gzip tar file ({ex.GetType().Name}).", ex);
        }
    }

    private static PackageContents ReadEntries(TarReader tar, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<TerminologyFile> files = [];
        (string Name, string Version, IReadOnlyList<string> FhirVersions)? manifest = null;
        int entries = 0;
        int other = 0;
        int ignored = 0;
        while (tar.GetNextEntry(copyData: false) is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entries > PackageLimits.MaxEntries)
            {
                throw new PackageFormatException($"The package has more than {PackageLimits.MaxEntries} entries.");
            }

            if (entry.EntryType is TarEntryType.Directory)
            {
                continue;
            }

            string path = entry.Name.TrimStart('.', '/');
            if (!seen.Add(path))
            {
                throw new PackageFormatException("The package repeats an entry path.");
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
            {
                ignored++;
                continue;
            }

            if (path == "package/" + PackageEntryFilter.ManifestName)
            {
                manifest = PackageManifestReader.Read(ReadData(entry, PackageLimits.MaxManifestBytes));
                continue;
            }

            if (PackageEntryFilter.FileName(path) is null)
            {
                ignored++;
                continue;
            }

            byte[] json = ReadData(entry, PackageLimits.MaxEntryBytes);
            if (PackageEntryFilter.IsTerminology(json))
            {
                files.Add(new TerminologyFile(path, json, Line: null, Offset: 0));
            }
            else
            {
                other++;
            }
        }

        if (manifest is not { } found)
        {
            throw new PackageFormatException("The package has no package/package.json.");
        }

        return new PackageContents(found.Name, found.Version, found.FhirVersions, files, other, ignored);
    }

    private static byte[] ReadData(TarEntry entry, int maxBytes)
    {
        if (entry.Length > maxBytes)
        {
            throw new PackageFormatException($"A package entry is larger than {maxBytes} bytes.");
        }

        byte[] data = new byte[entry.Length];
        entry.DataStream!.ReadExactly(data);
        return data;
    }
}
