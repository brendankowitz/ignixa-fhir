// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

internal static class GraphQlNamingHelper
{
    internal static readonly IReadOnlyList<FhirVersion> SupportedVersions =
        [FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6];

    internal static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    internal static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    internal static string ToBackboneTypeName(string resourceType, string elementPath)
        => $"{resourceType}_{ToPascalCase(elementPath)}";

    internal static string ToConnectionTypeName(string resourceType)
        => $"{resourceType}Connection";

    internal static string ToEdgeTypeName(string resourceType)
        => $"{resourceType}SearchEdge";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Schema names are lowercase by convention (fhir_r4, fhir_r5, etc.)")]
    internal static string GetSchemaName(FhirVersion version)
        => $"fhir_{version.ToString().ToLowerInvariant()}";
}
