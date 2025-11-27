// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Provides access to FHIR StructureDefinition metadata.
/// Implemented by version-specific structure providers (R4, R5, etc.).
/// </summary>
public interface ISchema
{
    /// <summary>
    /// FHIR version for this schema.
    /// </summary>
    FhirVersion Version { get; }

    /// <summary>
    /// Retrieves type metadata for the specified FHIR type.
    /// </summary>
    /// <param name="typeName">FHIR type name (e.g., "Patient", "HumanName", "string")</param>
    /// <returns>Type metadata or null if type not found</returns>
    IType? GetTypeDefinition(string typeName);

    /// <summary>
    /// Checks if the specified type name is a valid FHIR type in this schema.
    /// </summary>
    /// <param name="typeName">FHIR type name to check</param>
    /// <returns>True if the type is known in this schema; otherwise, false.</returns>
    bool IsKnownType(string typeName);
}
