// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sparky.Application.Features.Metadata.Models;
using Sparky.Domain.Models;
using Sparky.Extensions;

namespace Sparky.Application.Features.Metadata.Serialization;

/// <summary>
/// System.Text.Json converter that serializes ReferenceOrCanonicalJsonNode differently based on FHIR version:
/// - STU3: As Reference object {"reference": "...", "display": "..."}
/// - R4+: As simple canonical string "http://..."
/// </summary>
public class ReferenceOrCanonicalConverter : JsonConverter<ReferenceOrCanonicalJsonNode?>
{
    /// <summary>
    /// Key used in JsonSerializerOptions.TypeInfoResolver metadata to pass FHIR version.
    /// </summary>
    public const string FhirVersionKey = "FhirVersion";

    private readonly FhirSpecification _defaultVersion;

    public ReferenceOrCanonicalConverter()
        : this(FhirSpecification.R4)
    {
    }

    public ReferenceOrCanonicalConverter(FhirSpecification defaultVersion)
    {
        _defaultVersion = defaultVersion;
    }

    public override ReferenceOrCanonicalJsonNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Deserialization not needed for CapabilityStatement (write-only)
        throw new NotImplementedException("ReferenceOrCanonicalConverter does not support deserialization");
    }

    public override void Write(Utf8JsonWriter writer, ReferenceOrCanonicalJsonNode? value, JsonSerializerOptions options)
    {
        if (value == null || string.IsNullOrEmpty(value.Reference))
        {
            writer.WriteNullValue();
            return;
        }

        // Try to get FHIR version from options context (custom metadata)
        var fhirVersion = GetFhirVersionFromOptions(options) ?? _defaultVersion;

        if (fhirVersion == FhirSpecification.Stu3)
        {
            // STU3: Serialize as Reference object
            writer.WriteStartObject();
            writer.WriteString("reference", value.Reference);
            if (!string.IsNullOrEmpty(value.Display))
            {
                writer.WriteString("display", value.Display);
            }

            writer.WriteEndObject();
        }
        else
        {
            // R4, R4B, R5: Serialize as simple canonical string
            writer.WriteStringValue(value.Reference);
        }
    }

    /// <summary>
    /// Attempts to extract FhirSpecification from JsonSerializerOptions.
    /// Uses a custom property bag pattern since System.Text.Json doesn't have built-in context passing.
    /// </summary>
    private static FhirSpecification? GetFhirVersionFromOptions(JsonSerializerOptions options)
    {
        // Check if options has our custom TypeInfoResolver with metadata
        // This is set by CapabilityStatementSerializerOptions
        if (options.TypeInfoResolver is FhirVersionTypeInfoResolver resolver)
        {
            return resolver.FhirVersion;
        }

        return null;
    }
}
