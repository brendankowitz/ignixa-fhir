// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json.Serialization.Metadata;
using Ignixa.Domain.Models;
using Ignixa.Extensions;

namespace Ignixa.Application.Features.Metadata.Serialization;

/// <summary>
/// Custom TypeInfoResolver that carries FHIR version metadata for converters.
/// This enables version-aware serialization in ReferenceOrCanonicalConverter.
/// </summary>
public class FhirVersionTypeInfoResolver : DefaultJsonTypeInfoResolver
{
    public FhirVersionTypeInfoResolver(FhirSpecification fhirVersion)
    {
        FhirVersion = fhirVersion;
    }

    public FhirSpecification FhirVersion { get; }
}
