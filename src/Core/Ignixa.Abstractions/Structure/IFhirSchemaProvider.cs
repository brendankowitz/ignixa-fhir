// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Specification;

/// <summary>
/// Provides FHIR schema metadata for a specific FHIR version.
/// Extends <see cref="ISchema"/> for modern type metadata access.
/// </summary>
public interface IFhirSchemaProvider : ISchema
{
    /// <summary>
    /// Gets the FHIR specification version (e.g., Stu3, R4, R4B, R5, R6).
    /// </summary>
    new FhirVersion Version { get; }

    /// <summary>
    /// Gets the set of resource type names defined in this schema.
    /// Examples: "Patient", "Observation", "Bundle".
    /// </summary>
    IReadOnlySet<string> ResourceTypeNames { get; }

    /// <summary>
    /// Gets the full version string including patch and pre-release versions.
    /// Examples: "3.0.2", "4.0.1", "4.3.0", "5.0.0", "6.0.0-ballot2"
    /// </summary>
    string FullVersion { get; }

    /// <summary>
    /// Gets the reference metadata provider for this schema's FHIR version.
    /// Provides metadata about reference fields in FHIR resources.
    /// </summary>
    IReferenceMetadataProvider ReferenceMetadataProvider { get; }

    /// <summary>
    /// Gets the ValueSet provider for this schema's FHIR version.
    /// Provides access to ValueSet code definitions and validation.
    /// </summary>
    IValueSetProvider ValueSetProvider { get; }
}
