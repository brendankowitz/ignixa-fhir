// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Serialization;

namespace Ignixa.Search.Parsing;

/// <summary>
/// Factory for creating version-aware SearchOptionsBuilder instances.
/// </summary>
public interface ISearchOptionsBuilderFactory
{
    /// <summary>
    /// Creates a SearchOptionsBuilder for the specified FHIR version.
    /// Uses base search parameter definitions only (no tenant-specific IG parameters).
    /// </summary>
    /// <param name="fhirVersion">The FHIR version specification.</param>
    /// <returns>A SearchOptionsBuilder configured for the specified version.</returns>
    ISearchOptionsBuilder Create(FhirVersion fhirVersion);

    /// <summary>
    /// Creates a SearchOptionsBuilder for the specified FHIR version and tenant.
    /// Uses tenant-specific search parameter definitions including IG parameters (e.g., US Core).
    /// IMPORTANT: Use this overload for SQL Server searches to ensure query parameters match indexed values.
    /// </summary>
    /// <param name="fhirVersion">The FHIR version specification.</param>
    /// <param name="tenantId">The tenant ID (null uses base definitions only).</param>
    /// <returns>A SearchOptionsBuilder configured for the specified version and tenant.</returns>
    ISearchOptionsBuilder Create(FhirVersion fhirVersion, int? tenantId);
}
