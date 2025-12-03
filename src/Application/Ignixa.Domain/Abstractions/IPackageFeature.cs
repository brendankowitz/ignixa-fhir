// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Represents a server feature that implements operations from a FHIR package.
/// Declares what package the feature implements and what operations are supported.
/// Used by OperationsSegment to conditionally expose operations in CapabilityStatement.
/// </summary>
public interface IPackageFeature
{
    /// <summary>
    /// The FHIR package ID this feature implements (e.g., "hl7.fhir.uv.bulkdata").
    /// </summary>
    string PackageId { get; }

    /// <summary>
    /// System-level operations this feature implements (e.g., "export").
    /// Format: operation name only, without $ prefix.
    /// </summary>
    IReadOnlyList<string> SystemOperations { get; }

    /// <summary>
    /// Resource-level operations mapped by resource type (e.g., Patient => ["export"]).
    /// Format: operation names without $ prefix.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations { get; }

    /// <summary>
    /// Optional: FHIR versions this feature supports (null = all versions).
    /// Format: "R4", "R4B", "R5", "Stu3", etc.
    /// </summary>
    IReadOnlyList<string>? SupportedFhirVersions { get; }
}
