// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Immutable import job definition (input parameters) for use with BackgroundJob<ImportJobDefinition>.
/// Represents the configuration of a FHIR bulk import operation.
/// </summary>
public class ImportJobDefinition
{
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
