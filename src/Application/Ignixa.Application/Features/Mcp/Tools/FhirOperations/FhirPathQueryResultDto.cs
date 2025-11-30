// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Mcp.Tools.FhirOperations;

/// <summary>
/// DTO for FHIRPath CapabilityStatement query results.
/// </summary>
public class FhirPathQueryResultDto
{
    /// <summary>
    /// The FHIRPath expression that was evaluated.
    /// </summary>
    public required string Expression { get; init; }

    /// <summary>
    /// Number of results returned.
    /// </summary>
    public required int ResultCount { get; init; }

    /// <summary>
    /// Query results from FHIRPath evaluation.
    /// </summary>
    public required IReadOnlyList<object> Results { get; init; }

    /// <summary>
    /// Any errors that occurred during evaluation.
    /// </summary>
    public required IReadOnlyList<string> Errors { get; init; }
}
