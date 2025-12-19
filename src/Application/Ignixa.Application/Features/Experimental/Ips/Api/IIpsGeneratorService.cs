// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;

namespace Ignixa.Application.Features.Experimental.Ips.Api;

/// <summary>
/// Service for generating International Patient Summary (IPS) documents.
/// </summary>
public interface IIpsGeneratorService
{
    /// <summary>
    /// Generates an IPS document for a patient by ID.
    /// </summary>
    /// <param name="patientId">The patient ID.</param>
    /// <param name="profile">Optional specific IPS profile to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated IPS document bundle.</returns>
    Task<BundleJsonNode> GenerateIpsAsync(
        string patientId,
        string? profile = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an IPS document for a patient by identifier.
    /// </summary>
    /// <param name="identifierSystem">The identifier system.</param>
    /// <param name="identifierValue">The identifier value.</param>
    /// <param name="profile">Optional specific IPS profile to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated IPS document bundle.</returns>
    Task<BundleJsonNode> GenerateIpsByIdentifierAsync(
        string? identifierSystem,
        string identifierValue,
        string? profile = null,
        CancellationToken cancellationToken = default);
}
