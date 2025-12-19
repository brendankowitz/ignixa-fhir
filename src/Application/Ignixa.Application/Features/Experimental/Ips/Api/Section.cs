// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.Ips.Api;

/// <summary>
/// Represents a section in an IPS document with its configuration.
/// </summary>
public sealed record Section
{
    /// <summary>
    /// Section title for display (e.g., "Allergies and Intolerances").
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// LOINC code for the section (e.g., "48765-2").
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Code system URL (typically "http://loinc.org").
    /// </summary>
    public required string CodeSystem { get; init; }

    /// <summary>
    /// Display text for the LOINC code.
    /// </summary>
    public string? Display { get; init; }

    /// <summary>
    /// Profile URL for this section.
    /// </summary>
    public required string Profile { get; init; }

    /// <summary>
    /// Set of FHIR resource types that can appear in this section.
    /// </summary>
    public required IReadOnlySet<string> ResourceTypes { get; init; }

    /// <summary>
    /// Section cardinality (Required, Recommended, or Optional).
    /// </summary>
    public SectionCardinality Cardinality { get; init; } = SectionCardinality.Optional;
}

/// <summary>
/// IPS section cardinality as defined in the IPS IG.
/// </summary>
public enum SectionCardinality
{
    /// <summary>
    /// MUST include (medications, allergies, problems).
    /// </summary>
    Required,

    /// <summary>
    /// SHOULD include if data exists.
    /// </summary>
    Recommended,

    /// <summary>
    /// MAY include.
    /// </summary>
    Optional
}
