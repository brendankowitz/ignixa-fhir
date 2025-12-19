// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.Ips.Api;

/// <summary>
/// Strategy for generating IPS documents.
/// Allows customization for different jurisdictions (default, US, EU, etc.).
/// </summary>
public interface IIpsGenerationStrategy
{
    /// <summary>
    /// Bundle profile URL this strategy produces.
    /// </summary>
    string BundleProfile { get; }

    /// <summary>
    /// Gets the ordered list of sections to include in the IPS.
    /// </summary>
    IReadOnlyList<Section> GetSections();

    /// <summary>
    /// Determines if a resource should be included in a specific section.
    /// </summary>
    bool ShouldIncludeResource(Section section, ResourceJsonNode resource, IpsContext context);

    /// <summary>
    /// Classifies a resource into its appropriate IPS section.
    /// Returns null if the resource doesn't belong to any section.
    /// </summary>
    Section? ClassifyResource(ResourceJsonNode resource);

    /// <summary>
    /// Creates the document author (Organization or Device).
    /// </summary>
    ResourceJsonNode CreateAuthor(IpsContext context);

    /// <summary>
    /// Creates the document title.
    /// </summary>
    string CreateTitle(IpsContext context);

    /// <summary>
    /// Post-processing hook after bundle assembly.
    /// </summary>
    void PostProcessBundle(ResourceJsonNode bundle, IpsContext context);
}
