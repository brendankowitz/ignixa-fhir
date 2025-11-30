// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Extension methods for <see cref="FhirPrimitive"/> enum.
/// </summary>
public static class FhirPrimitiveExtensions
{
    /// <summary>
    /// Checks if the primitive type is supported in the specified FHIR version.
    /// </summary>
    /// <param name="primitive">The primitive type to check.</param>
    /// <param name="version">The FHIR version to check against.</param>
    /// <returns>True if the primitive type is supported in the specified version.</returns>
    public static bool IsSupportedIn(this FhirPrimitive primitive, FhirVersion version)
    {
        return primitive switch
        {
            FhirPrimitive.Integer64 => version >= FhirVersion.R5,
            FhirPrimitive.Url or FhirPrimitive.Canonical or FhirPrimitive.Uuid
                => version >= FhirVersion.R4,
            FhirPrimitive.None => false,
            _ => true  // All others supported since STU3
        };
    }

    /// <summary>
    /// Converts <see cref="FhirPrimitive"/> enum to FHIR type string.
    /// </summary>
    /// <param name="primitive">The primitive type to convert.</param>
    /// <returns>The FHIR type name as a string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the primitive type is unknown.</exception>
    public static string ToTypeString(this FhirPrimitive primitive)
    {
        return primitive switch
        {
            FhirPrimitive.Boolean => "boolean",
            FhirPrimitive.Integer => "integer",
            FhirPrimitive.String => "string",
            FhirPrimitive.Decimal => "decimal",
            FhirPrimitive.Uri => "uri",
            FhirPrimitive.Base64Binary => "base64Binary",
            FhirPrimitive.Instant => "instant",
            FhirPrimitive.Date => "date",
            FhirPrimitive.DateTime => "dateTime",
            FhirPrimitive.Time => "time",
            FhirPrimitive.Code => "code",
            FhirPrimitive.Oid => "oid",
            FhirPrimitive.Id => "id",
            FhirPrimitive.Markdown => "markdown",
            FhirPrimitive.UnsignedInt => "unsignedInt",
            FhirPrimitive.PositiveInt => "positiveInt",
            FhirPrimitive.Url => "url",
            FhirPrimitive.Canonical => "canonical",
            FhirPrimitive.Uuid => "uuid",
            FhirPrimitive.Integer64 => "integer64",
            FhirPrimitive.None => throw new ArgumentOutOfRangeException(nameof(primitive), primitive, "Cannot convert None to type string"),
            _ => throw new ArgumentOutOfRangeException(nameof(primitive), primitive, "Unknown primitive type")
        };
    }

    /// <summary>
    /// Parses FHIR type string to <see cref="FhirPrimitive"/> enum.
    /// </summary>
    /// <param name="typeName">The FHIR type name to parse.</param>
    /// <returns>The corresponding <see cref="FhirPrimitive"/> value, or <see cref="FhirPrimitive.None"/> if not a primitive type.</returns>
    public static FhirPrimitive FromTypeString(string typeName)
    {
        return typeName switch
        {
            "boolean" => FhirPrimitive.Boolean,
            "integer" => FhirPrimitive.Integer,
            "string" => FhirPrimitive.String,
            "decimal" => FhirPrimitive.Decimal,
            "uri" => FhirPrimitive.Uri,
            "base64Binary" => FhirPrimitive.Base64Binary,
            "instant" => FhirPrimitive.Instant,
            "date" => FhirPrimitive.Date,
            "dateTime" => FhirPrimitive.DateTime,
            "time" => FhirPrimitive.Time,
            "code" => FhirPrimitive.Code,
            "oid" => FhirPrimitive.Oid,
            "id" => FhirPrimitive.Id,
            "markdown" => FhirPrimitive.Markdown,
            "unsignedInt" => FhirPrimitive.UnsignedInt,
            "positiveInt" => FhirPrimitive.PositiveInt,
            "url" => FhirPrimitive.Url,
            "canonical" => FhirPrimitive.Canonical,
            "uuid" => FhirPrimitive.Uuid,
            "integer64" => FhirPrimitive.Integer64,
            _ => FhirPrimitive.None
        };
    }
}
