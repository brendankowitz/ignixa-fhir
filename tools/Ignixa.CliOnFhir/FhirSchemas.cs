// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Specification.Generated;

namespace Ignixa.CliOnFhir;

/// <summary>Maps the <c>--fhir-version</c> option to Ignixa's generated schema providers.</summary>
public static class FhirSchemas
{
    public const string DefaultVersion = "r4";

    public static IReadOnlyList<string> SupportedVersions { get; } = ["stu3", "r4", "r4b", "r5"];

    public static IFhirSchemaProvider Resolve(string version) => version.ToUpperInvariant() switch
    {
        "STU3" => new STU3CoreSchemaProvider(),
        "R4" => new R4CoreSchemaProvider(),
        "R4B" => new R4BCoreSchemaProvider(),
        "R5" => new R5CoreSchemaProvider(),
        _ => throw new ArgumentException(
            $"Unsupported FHIR version '{version}'. Supported: {string.Join(", ", SupportedVersions)}.",
            nameof(version)),
    };
}
