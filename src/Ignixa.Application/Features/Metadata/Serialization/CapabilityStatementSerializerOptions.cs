// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Ignixa.Domain.Models;
using Ignixa.Extensions;

namespace Ignixa.Application.Features.Metadata.Serialization;

/// <summary>
/// Factory for creating JsonSerializerOptions configured for version-aware CapabilityStatement serialization.
/// </summary>
public static class CapabilityStatementSerializerOptions
{
    /// <summary>
    /// Creates JsonSerializerOptions configured for a specific FHIR version.
    /// Includes version-aware converters like ReferenceOrCanonicalConverter.
    /// </summary>
    /// <param name="fhirVersion">The FHIR version to serialize for (STU3, R4, R4B, R5).</param>
    /// <returns>Configured JsonSerializerOptions.</returns>
    public static JsonSerializerOptions Create(FhirSpecification fhirVersion)
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,

            // Use custom type info resolver to pass FHIR version to converters
            TypeInfoResolver = new FhirVersionTypeInfoResolver(fhirVersion),
        };

        // Add version-aware converters
        options.Converters.Add(new ReferenceOrCanonicalConverter(fhirVersion));

        // Add standard enum converter (for Status, Kind, etc.)
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }

    /// <summary>
    /// Creates JsonSerializerOptions with default FHIR version (R4).
    /// </summary>
    public static JsonSerializerOptions CreateDefault()
    {
        return Create(FhirSpecification.R4);
    }
}
