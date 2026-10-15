// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Terminology.Packages;

/// <summary>Reads <c>package/package.json</c>: name, version and FHIR versions (<c>fhirVersions</c>, <c>fhir-version-list</c> or <c>fhirVersion</c>).</summary>
internal static class PackageManifestReader
{
    public static (string Name, string Version, IReadOnlyList<string> FhirVersions) Read(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            if (TerminologyJson.NonEmpty(root, "name") is not { } name || TerminologyJson.NonEmpty(root, "version") is not { } version)
            {
                throw new PackageFormatException("package.json has no name or no version.");
            }

            // fhirVersions is the current spelling; the R4-era core packages use fhir-version-list.
            List<string> fhirVersions = [.. TerminologyJson.Array(root, "fhirVersions").Concat(TerminologyJson.Array(root, "fhir-version-list"))
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!)
                .Distinct(StringComparer.Ordinal)];
            if (fhirVersions.Count == 0 && TerminologyJson.NonEmpty(root, "fhirVersion") is { } single)
            {
                fhirVersions.Add(single);
            }

            return (name, version, fhirVersions);
        }
        catch (JsonException ex)
        {
            throw new PackageFormatException("package.json is not valid JSON.", ex);
        }
    }
}
