// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>Options controlling how <see cref="ISearchResponseComposer"/> shapes a graph into response bundles.</summary>
public sealed record SearchResponseOptions
{
    /// <summary>The search URL bundles are a response to (used for <c>Bundle.link</c> <c>self</c>/<c>next</c>/<c>previous</c>).</summary>
    public required string SearchUrl { get; init; }

    /// <summary>Base URL prepended to <see cref="SearchUrl"/> for links and to resource references for <c>fullUrl</c>. Defaults to a placeholder suitable for fixture generation.</summary>
    public string BaseUrl { get; init; } = "http://localhost/fhir";

    /// <summary>The primary resource type this search matched (e.g. "Appointment"). Other types in the graph are includes.</summary>
    public required string MatchResourceType { get; init; }

    /// <summary>The bundle type to emit. Defaults to <see cref="ResponseBundleType.Searchset"/>.</summary>
    public ResponseBundleType BundleType { get; init; } = ResponseBundleType.Searchset;

    /// <summary>Maximum matching entries per page. Defaults to 20.</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>Whether included (non-matching) resources are present or omitted. Defaults to <see cref="IncludeCompleteness.Complete"/>.</summary>
    public IncludeCompleteness IncludeCompleteness { get; init; } = IncludeCompleteness.Complete;
}
