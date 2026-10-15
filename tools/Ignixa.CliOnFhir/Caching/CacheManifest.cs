// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.Search.Definition;
using Ignixa.Specification.Generated;

namespace Ignixa.CliOnFhir.Caching;

/// <summary>
/// Everything an index depends on besides the resources' content: the cache format, the FHIR version, the tool
/// build, the search parameter definitions, and the folder's files as the catalog saw them. A cache is valid
/// only for a manifest equal to the current one.
/// </summary>
/// <param name="FormatVersion">Layout version of the cache files; bump it whenever the persisted shape changes.</param>
/// <param name="FolderPath">Absolute path of the folder the cache was built for.</param>
/// <param name="FhirVersion">FHIR version the folder was read as.</param>
/// <param name="ToolVersion">Informational version of the tool assembly that wrote the cache.</param>
/// <param name="DefinitionsHash">Hash over the search parameter definitions the indexer used.</param>
/// <param name="ModuleVersions">
/// Module version ids of the assemblies that build indexes (the tool, Ignixa.Search, Ignixa.Specification). They
/// derive from the compiled content, so an uncommitted code change invalidates the cache like a release would.
/// </param>
/// <param name="Files">The catalogued files with the size and last-write time they had when read.</param>
internal sealed record CacheManifest(
    int FormatVersion,
    string FolderPath,
    string FhirVersion,
    string ToolVersion,
    string DefinitionsHash,
    string ModuleVersions,
    IReadOnlyList<CatalogFile> Files)
{
    public const int CurrentFormatVersion = 1;

    public static CacheManifest For(FolderCatalog catalog, string folderPath, string fhirVersion, ISearchParameterDefinitionManager definitions)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(definitions);

        return new CacheManifest(
            CurrentFormatVersion,
            folderPath,
            fhirVersion,
            CurrentToolVersion(),
            HashDefinitions(definitions),
            CurrentModuleVersions(),
            [.. catalog.Files.OrderBy(file => file.Path, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Checks that every member is present and no file is listed twice. JSON can hold a null for any member, so a
    /// deserialized manifest is not trusted until this passes.
    /// </summary>
    /// <exception cref="InvalidDataException">A member is missing or a path repeats.</exception>
    public void Validate()
    {
        if (FolderPath is null || FhirVersion is null || ToolVersion is null || DefinitionsHash is null || ModuleVersions is null || Files is null)
        {
            throw new InvalidDataException("a required member of the manifest is missing.");
        }

        HashSet<string> paths = new(StringComparer.Ordinal);
        foreach (CatalogFile file in Files)
        {
            if (file?.Path is null)
            {
                throw new InvalidDataException("a file entry in the manifest is incomplete.");
            }

            if (!paths.Add(file.Path))
            {
                throw new InvalidDataException($"the manifest lists '{file.Path}' twice.");
            }
        }
    }

    /// <summary>
    /// Compares this stored manifest with the manifest of the current run. Returns null when they match;
    /// otherwise why they differ and how many files were added, removed or modified.
    /// </summary>
    public ManifestMismatch? Compare(CacheManifest current)
    {
        ArgumentNullException.ThrowIfNull(current);

        List<string> reasons = [];
        AddIfDifferent(reasons, "cache format", FormatVersion.ToString(CultureInfo.InvariantCulture), current.FormatVersion.ToString(CultureInfo.InvariantCulture));
        AddIfDifferent(reasons, "folder path", FolderPath, current.FolderPath);
        AddIfDifferent(reasons, "FHIR version", FhirVersion, current.FhirVersion);
        AddIfDifferent(reasons, "tool version", ToolVersion, current.ToolVersion);
        AddIfDifferent(reasons, "search parameter definitions", DefinitionsHash, current.DefinitionsHash);
        AddIfDifferent(reasons, "indexing code", ModuleVersions, current.ModuleVersions);

        Dictionary<string, CatalogFile> cached = Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        int added = 0;
        int modified = 0;
        foreach (CatalogFile file in current.Files)
        {
            if (!cached.Remove(file.Path, out CatalogFile? previous))
            {
                added++;
            }
            else if (previous != file)
            {
                modified++;
            }
        }

        int removed = cached.Count;
        int changed = added + modified + removed;
        if (changed > 0)
        {
            reasons.Add($"{changed} of the folder's files changed since the cache was written ({added} added, {removed} removed, {modified} modified)");
        }

        return reasons.Count == 0 ? null : new ManifestMismatch(string.Join("; ", reasons), changed);
    }

    private static void AddIfDifferent(List<string> reasons, string what, string cached, string current)
    {
        if (!string.Equals(cached, current, StringComparison.Ordinal))
        {
            reasons.Add($"{what} differs (cached '{cached}', current '{current}')");
        }
    }

    private static string CurrentToolVersion()
    {
        Assembly assembly = typeof(CacheManifest).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? throw new InvalidOperationException("The tool assembly carries no version to key the index cache on.");
    }

    private static string CurrentModuleVersions() => string.Join(
        ';',
        new[] { typeof(CacheManifest).Assembly, typeof(SearchParameterDefinitionManager).Assembly, typeof(R4CoreSchemaProvider).Assembly }
            .Distinct()
            .Select(assembly => $"{assembly.GetName().Name}={assembly.ManifestModule.ModuleVersionId}")
            .Order(StringComparer.Ordinal));

    // Covers what decides whether a cached entry still means the same thing: which parameters exist per
    // resource type (the code rebinding relies on), their type, and the expression they were extracted with.
    private static string HashDefinitions(ISearchParameterDefinitionManager definitions)
    {
        IEnumerable<string> lines = definitions.AllSearchParameters
            .Select(parameter => string.Join(
                '|',
                parameter.Url?.ToString() ?? string.Empty,
                parameter.Code,
                parameter.Type,
                parameter.Expression,
                string.Join(',', parameter.BaseResourceTypes.Order(StringComparer.Ordinal))))
            .Order(StringComparer.Ordinal);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string line in lines)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(line));
            hash.AppendData("\n"u8);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
