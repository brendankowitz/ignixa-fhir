// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Ignixa.Application.Events.Terminology;

/// <summary>
/// Event published when terminology import should be triggered for a package.
/// Consumed by DurableTask orchestration to start background import.
/// </summary>
public interface ITerminologyImportTriggered : INotification
{
    /// <summary>
    /// Tenant ID for the import (multi-tenancy support).
    /// </summary>
    int TenantId { get; }

    /// <summary>
    /// Package ID (e.g., "hl7.fhir.us.core").
    /// </summary>
    string PackageId { get; }

    /// <summary>
    /// Package version (e.g., "5.0.1").
    /// </summary>
    string PackageVersion { get; }

    /// <summary>
    /// List of PackageResourceIds to import (filtered to CodeSystem/ValueSet/ConceptMap).
    /// </summary>
    IReadOnlyList<long> PackageResourceIds { get; }
}
