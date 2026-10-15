// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Packages;

/// <summary>
/// Reads an extracted package directory (<c>&lt;dir&gt;/package/package.json</c> and the JSON files beside it), as the
/// standard FHIR package cache holds them under <c>~/.fhir/packages/&lt;id&gt;#&lt;version&gt;</c>. Read only, with the
/// same per-file and entry-count limits as an archive; subfolders are not read.
/// </summary>
internal static class PackageDirectoryReader
{
    /// <exception cref="PackageFormatException">No usable package.json, or a file over a limit.</exception>
    /// <exception cref="IOException">The directory cannot be read.</exception>
    public static PackageContents Read(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string packageDirectory = Path.Combine(directory, "package");
        string manifestPath = Path.Combine(packageDirectory, PackageEntryFilter.ManifestName);
        if (!File.Exists(manifestPath))
        {
            throw new PackageFormatException("The package directory has no package/package.json.");
        }

        (string name, string version, IReadOnlyList<string> fhirVersions) = PackageManifestReader.Read(ReadFile(manifestPath, PackageLimits.MaxManifestBytes));
        List<string> paths = [.. Directory.EnumerateFiles(packageDirectory, "*", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal)];
        if (paths.Count > PackageLimits.MaxEntries)
        {
            throw new PackageFormatException($"The package directory has more than {PackageLimits.MaxEntries} files.");
        }

        List<TerminologyFile> files = [];
        int other = 0;
        int ignored = 0;
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = "package/" + Path.GetFileName(path);
            if (relative == "package/" + PackageEntryFilter.ManifestName)
            {
                continue;
            }

            if (PackageEntryFilter.FileName(relative) is null)
            {
                ignored++;
                continue;
            }

            byte[] json = ReadFile(path, PackageLimits.MaxEntryBytes);
            if (PackageEntryFilter.IsTerminology(json))
            {
                files.Add(new TerminologyFile(relative, json, Line: null, Offset: 0));
            }
            else
            {
                other++;
            }
        }

        return new PackageContents(name, version, fhirVersions, files, other, ignored);
    }

    private static byte[] ReadFile(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxBytes)
        {
            throw new PackageFormatException($"A package file is larger than {maxBytes} bytes.");
        }

        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }
}
