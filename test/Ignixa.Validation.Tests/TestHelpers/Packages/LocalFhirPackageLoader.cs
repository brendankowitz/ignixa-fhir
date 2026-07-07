// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Text.Json;
using Ignixa.PackageManagement.Models;

namespace Ignixa.Validation.Tests.TestHelpers.Packages;

/// <summary>
/// Loads conformance resources from an <b>already-unpacked</b> local FHIR package directory (the
/// <c>package/</c> folder of loose <c>*.json</c> files under <c>~/.fhir/packages</c>). This is the
/// offline counterpart to <see cref="TestFhirPackageLoader"/> (which downloads tarballs): it lets
/// the conformance runner and profile tests resolve against the core package with no network.
/// </summary>
public static class LocalFhirPackageLoader
{
    /// <summary>Standard local cache folder name for the R4 core package.</summary>
    public const string R4CorePackageFolder = "hl7.fhir.r4.core#4.0.1";

    private static readonly FrozenSet<string> ConformanceResourceTypes = new[]
    {
        "StructureDefinition",
        "ValueSet",
        "CodeSystem",
        "ConceptMap",
        "NamingSystem",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the <c>package/</c> directory for a package id#version under the FHIR package cache
    /// (<c>%FHIR_PACKAGE_CACHE%</c> or <c>~/.fhir/packages</c>). The directory may not exist.
    /// </summary>
    /// <param name="packageFolderName">Cache folder name, e.g. <c>hl7.fhir.r4.core#4.0.1</c>.</param>
    public static string GetFhirCachePackageDirectory(string packageFolderName)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageFolderName);

        var cacheRoot = Environment.GetEnvironmentVariable("FHIR_PACKAGE_CACHE");
        if (string.IsNullOrEmpty(cacheRoot))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            cacheRoot = Path.Combine(home, ".fhir", "packages");
        }

        return Path.Combine(cacheRoot, packageFolderName, "package");
    }

    /// <summary>
    /// Loads the R4 core package from the local FHIR cache, or returns null when it is not present.
    /// </summary>
    public static IReadOnlyList<ExtractedResource>? TryLoadR4Core()
        => TryLoadFromPackageDirectory(GetFhirCachePackageDirectory(R4CorePackageFolder));

    /// <summary>
    /// Reads every conformance resource JSON from an unpacked package directory. Returns null when
    /// the directory is missing; skips non-conformance JSON and files without a resource type, url,
    /// or id (mirroring the tarball extractor's filtering).
    /// </summary>
    /// <param name="packageDirectory">The <c>package/</c> folder of loose FHIR JSON files.</param>
    public static IReadOnlyList<ExtractedResource>? TryLoadFromPackageDirectory(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageDirectory);

        if (!Directory.Exists(packageDirectory))
        {
            return null;
        }

        var fhirVersion = ReadManifestFhirVersion(packageDirectory);
        var resources = new List<ExtractedResource>();
        foreach (var file in Directory.EnumerateFiles(packageDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var resource = TryReadResource(file, fhirVersion);
            if (resource is not null)
            {
                resources.Add(resource);
            }
        }

        return resources;
    }

    private static string ReadManifestFhirVersion(string packageDirectory)
    {
        var manifestPath = Path.Combine(packageDirectory, "package.json");
        if (!File.Exists(manifestPath))
        {
            return "4.0.1";
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return doc.RootElement.TryGetProperty("fhirVersion", out var fv) && fv.ValueKind == JsonValueKind.String
                ? fv.GetString() ?? "4.0.1"
                : "4.0.1";
        }
        catch (JsonException)
        {
            return "4.0.1";
        }
    }

    private static ExtractedResource? TryReadResource(string file, string fhirVersion)
    {
        try
        {
            var content = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var resourceType = GetString(root, "resourceType");
            if (resourceType is null || !ConformanceResourceTypes.Contains(resourceType))
            {
                return null;
            }

            var canonical = GetString(root, "url");
            var resourceId = GetString(root, "id");
            if (canonical is null || resourceId is null)
            {
                return null;
            }

            return new ExtractedResource
            {
                ResourceType = resourceType,
                Canonical = canonical,
                Version = GetString(root, "version"),
                ResourceId = resourceId,
                ResourceJson = content,
                FhirVersion = fhirVersion,
            };
        }
        catch (JsonException)
        {
            // Not a FHIR resource JSON — skip, matching the tarball extractor.
            return null;
        }
    }

    private static string? GetString(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
