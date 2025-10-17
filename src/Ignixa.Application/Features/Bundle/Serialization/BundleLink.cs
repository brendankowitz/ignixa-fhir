// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Bundle.Serialization;

/// <summary>
/// Represents a link in a FHIR bundle (self, next, prev, etc.).
/// Extracted from bundle.link array during header parsing.
/// </summary>
public class BundleLink
{
    /// <summary>
    /// Gets the link relation type (e.g., "self", "next", "prev", "first", "last").
    /// </summary>
    public required string Relation { get; init; }

    /// <summary>
    /// Gets the URL of the link.
    /// </summary>
    public required string Url { get; init; }
}
