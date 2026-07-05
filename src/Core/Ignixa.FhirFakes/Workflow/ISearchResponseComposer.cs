// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Emits a resource graph as one or more FHIR search response bundles. Owns response-level shaping
/// (bundle type, paging, include completeness) so packs stay focused on graph assembly.
/// </summary>
public interface ISearchResponseComposer
{
    /// <summary>
    /// Composes <paramref name="graph"/> into one bundle per page. A graph with no matching entries
    /// still returns exactly one (empty) page, never an empty list.
    /// </summary>
    IReadOnlyList<BundleJsonNode> Compose(ResourceGraph graph, SearchResponseOptions options);
}
