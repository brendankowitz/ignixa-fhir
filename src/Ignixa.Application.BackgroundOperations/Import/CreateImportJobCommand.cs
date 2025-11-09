// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Ignixa.Serialization.Models;
using Medino;

namespace Ignixa.Application.BackgroundOperations.Import;

/// <summary>
/// Command to create and start a new FHIR bulk import job.
/// Validates input parameters and initiates the import orchestration.
/// </summary>
public record CreateImportJobCommand : IRequest<CreateImportJobResult>
{
    /// <summary>
    /// Tenant ID for multi-tenancy isolation.
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Input files to import. Must have at least one file.
    /// </summary>
    public required IReadOnlyList<InputFileInfo> InputFiles { get; init; }

    /// <summary>
    /// Import mode: "InitialLoad" (replace all data) or "IncrementalLoad" (merge/update).
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// Batch size for processing resources (default: 100).
    /// </summary>
    public int BatchSize { get; init; }

    /// <summary>
    /// Channel capacity for producer/consumer pattern (default: 1000).
    /// </summary>
    public int ChannelCapacity { get; init; }

    /// <summary>
    /// Optional storage detail parameters (for custom storage configurations).
    /// </summary>
    public ParametersJsonNode? StorageDetail { get; init; }
}
