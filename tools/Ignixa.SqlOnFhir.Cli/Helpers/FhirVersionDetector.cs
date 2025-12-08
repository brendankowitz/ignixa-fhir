// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization;
using Ignixa.Specification;

namespace Ignixa.SqlOnFhir.Cli.Helpers;

/// <summary>
/// Helper class for FHIR version detection from resource files.
/// </summary>
internal static class FhirVersionDetector
{
    /// <summary>
    /// Detects FHIR version from an NDJSON file containing FHIR resources.
    /// Currently defaults to R4 as the most common version.
    /// </summary>
    /// <param name="inputPath">Path to NDJSON file</param>
    /// <returns>FHIR schema provider for detected version, or null if file is empty</returns>
    /// <remarks>
    /// TODO: Implement proper FHIR version detection by examining meta.profile or fhirVersion
    /// in the resource to determine whether to use STU3, R4, R4B, R5, or R6 schema provider.
    /// </remarks>
    public static async Task<IFhirSchemaProvider?> DetectFhirVersionAsync(string inputPath)
    {
        await foreach (var line in File.ReadLinesAsync(inputPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var resourceNode = JsonSourceNodeFactory.Parse(line);
            if (resourceNode == null)
            {
                continue;
            }

            // Default to R4 for now - the most common FHIR version
            // Future enhancement: detect from meta.profile or fhirVersion field
            return new Specification.Generated.R4CoreSchemaProvider();
        }

        return null;
    }
}
