// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Sparky.Application.Features.Metadata.Models;

/// <summary>
/// Represents a property that serializes differently between FHIR versions:
/// - STU3: Reference object with "reference" and "display" properties
/// - R4+: Simple canonical string
/// Used for profile, instantiates, and implementationGuide properties.
/// </summary>
public class ReferenceOrCanonicalJsonNode
{
    /// <summary>
    /// The canonical URL or reference string.
    /// </summary>
    public string? Reference { get; set; }

    /// <summary>
    /// Optional display text (primarily for STU3 Reference objects).
    /// </summary>
    public string? Display { get; set; }

    /// <summary>
    /// Creates a ReferenceOrCanonicalJsonNode from a canonical URL.
    /// </summary>
    public static ReferenceOrCanonicalJsonNode FromCanonical(string canonicalUrl, string? display = null)
    {
        return new ReferenceOrCanonicalJsonNode
        {
            Reference = canonicalUrl,
            Display = display,
        };
    }

    /// <summary>
    /// Implicit conversion from string to ReferenceOrCanonicalJsonNode.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "FromCanonical provides named alternative")]
    public static implicit operator ReferenceOrCanonicalJsonNode?(string? canonicalUrl)
    {
        return canonicalUrl == null ? null : new ReferenceOrCanonicalJsonNode { Reference = canonicalUrl };
    }

    /// <summary>
    /// Implicit conversion from ReferenceOrCanonicalJsonNode to string.
    /// </summary>
    public static implicit operator string?(ReferenceOrCanonicalJsonNode? node)
    {
        return node?.Reference;
    }

    public override string ToString()
    {
        return Reference ?? string.Empty;
    }
}
