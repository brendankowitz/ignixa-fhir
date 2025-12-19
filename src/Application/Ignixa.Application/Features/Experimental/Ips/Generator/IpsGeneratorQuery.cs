// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;
using Medino;

namespace Ignixa.Application.Features.Experimental.Ips.Generator;

/// <summary>
/// Query to generate an International Patient Summary (IPS) document.
/// </summary>
/// <param name="PatientId">The patient ID (if known).</param>
/// <param name="PatientIdentifier">The patient identifier token (system|value format) if ID not known.</param>
/// <param name="Profile">Optional specific IPS profile to generate.</param>
public record IpsGeneratorQuery(
    string? PatientId,
    string? PatientIdentifier,
    string? Profile) : IRequest<IpsGeneratorResult>;

/// <summary>
/// Result of IPS generation.
/// </summary>
/// <param name="IpsBundle">The generated IPS document bundle.</param>
/// <param name="Metrics">Generation metrics.</param>
public record IpsGeneratorResult(
    BundleJsonNode IpsBundle,
    IpsGenerationMetrics Metrics);

/// <summary>
/// Metrics for IPS generation.
/// </summary>
/// <param name="TotalResources">Total resources in the bundle.</param>
/// <param name="SectionsIncluded">Number of sections with resources.</param>
/// <param name="SectionsEmpty">Number of empty required sections.</param>
/// <param name="TotalDuration">Total generation time.</param>
public record IpsGenerationMetrics(
    int TotalResources,
    int SectionsIncluded,
    int SectionsEmpty,
    TimeSpan TotalDuration);
