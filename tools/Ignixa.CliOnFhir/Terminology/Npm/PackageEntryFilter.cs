// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Terminology.Npm;

/// <summary>
/// Decides which package files the store keeps, the same way for an archive and a directory: JSON files directly under
/// <c>package/</c> (not <c>.index.json</c> or other dot-files, not subfolders such as <c>package/example</c>) that are
/// CodeSystem, ValueSet or ConceptMap, plus those whose type cannot be read, so they are reported as unusable; and, when a
/// caller validates against profiles, StructureDefinitions.
/// </summary>
internal static class PackageEntryFilter
{
    public const string ManifestName = "package.json";

    /// <summary>The file name when <paramref name="relativePath"/> (forward slashes, from the package root) is read, otherwise null.</summary>
    public static string? FileName(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        const string Prefix = "package/";
        if (!relativePath.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string name = relativePath[Prefix.Length..];
        return name.Length > 0 && !name.Contains('/', StringComparison.Ordinal) && !name.StartsWith('.') && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? name
            : null;
    }

    /// <summary>Whether to keep the file: a terminology resource, or JSON whose top level cannot be read.</summary>
    public static bool IsTerminology(ReadOnlySpan<byte> json)
    {
        try
        {
            return ResourceSniffer.Sniff(json).ResourceType is "CodeSystem" or "ValueSet" or "ConceptMap";
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>Whether the file is a StructureDefinition (one whose type cannot be read is not).</summary>
    public static bool IsStructureDefinition(ReadOnlySpan<byte> json)
    {
        try
        {
            return ResourceSniffer.Sniff(json).ResourceType == "StructureDefinition";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
