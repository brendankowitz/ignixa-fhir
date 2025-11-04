// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;

namespace Ignixa.Domain.Models;

/// <summary>
/// Immutable import job definition (input parameters) for use with BackgroundJob<ImportJobDefinition>.
/// Represents the configuration of a FHIR bulk import operation.
/// TenantId is stored here (in the payload), not as a BackgroundJob property.
/// </summary>
public class ImportJobDefinition : IJobDefinition
{
    /// <summary>
    /// Tenant ID for multi-tenancy isolation (stored in definition payload, not schema).
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Input format (must be "application/fhir+ndjson").
    /// </summary>
    public required string InputFormat { get; init; }

    /// <summary>
    /// Input source description (e.g., "Patient", "Observation").
    /// </summary>
    public required string InputSource { get; init; }

    /// <summary>
    /// Import mode: "InitialLoad" or "IncrementalLoad".
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// List of input files to import.
    /// </summary>
    public required IReadOnlyList<InputFileInfo> InputFiles { get; init; }
}
