// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.Ips.Api;

/// <summary>
/// Context for IPS document generation.
/// </summary>
public sealed record IpsContext
{
    /// <summary>
    /// The patient ID for which the IPS is being generated.
    /// </summary>
    public required string PatientId { get; init; }

    /// <summary>
    /// The patient resource.
    /// </summary>
    public required ResourceJsonNode Patient { get; init; }

    /// <summary>
    /// The generation strategy being used.
    /// </summary>
    public required IIpsGenerationStrategy Strategy { get; init; }

    /// <summary>
    /// The tenant partition ID.
    /// </summary>
    public required int PartitionId { get; init; }

    /// <summary>
    /// Timestamp when the IPS was generated.
    /// </summary>
    public required DateTimeOffset GenerationTime { get; init; }
}
