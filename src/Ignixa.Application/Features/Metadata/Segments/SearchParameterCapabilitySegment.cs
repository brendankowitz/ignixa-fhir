// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Search.Infrastructure;
using Ignixa.Search.Definition;
using Ignixa.Specification.ValueSets.Normative;
using IgnixaSearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Search parameter capability segment.
/// Populates search parameters for each resource type from search parameter definitions.
/// Changes when search parameters are added/removed or custom search parameters registered.
/// </summary>
public class SearchParameterCapabilitySegment : ICapabilitySegment
{
    private readonly IFhirVersionContext _versionContext;
    private readonly ILogger<SearchParameterCapabilitySegment> _logger;

    public SearchParameterCapabilitySegment(
        IFhirVersionContext versionContext,
        ILogger<SearchParameterCapabilitySegment> logger)
    {
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string SegmentKey => "search-params";

    public int Priority => 30; // Execute after interactions

    public ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying search parameter capability segment for {FhirVersion}", context.FhirVersion);

        // Get manager for this FHIR version
        var manager = _versionContext.GetSearchParameterDefinitionManager(context.FhirVersion);

        if (statement.Rest == null || statement.Rest.Count == 0)
        {
            _logger.LogWarning("No REST component found in capability statement - search parameters will not be added");
            return ValueTask.CompletedTask;
        }

        var restComponent = statement.Rest[0];
        if (restComponent.Resource == null)
        {
            _logger.LogWarning("No resources found in REST component - search parameters will not be added");
            return ValueTask.CompletedTask;
        }

        int totalSearchParams = 0;

        // Populate search parameters for each resource
        foreach (var resource in restComponent.Resource)
        {
            if (!manager.TryGetSearchParameters(resource.Type, out var searchParamsEnumerable))
            {
                continue;
            }

            var searchParams = searchParamsEnumerable.ToList();
            if (searchParams.Count == 0)
            {
                continue;
            }

            // Build search parameter list
            resource.SearchParam = BuildSearchParameters(searchParams);
            totalSearchParams += resource.SearchParam.Count;
        }

        _logger.LogDebug("Added {Count} total search parameters across {ResourceCount} resources",
            totalSearchParams, restComponent.Resource.Count);

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Hash is based on all search parameter URLs
        var manager = _versionContext.GetSearchParameterDefinitionManager(context.FhirVersion);

        var allSearchParams = manager.AllSearchParameters
            .Where(sp => sp.IsSupported)
            .Select(sp => sp.Url?.ToString() ?? sp.Code)
            .OrderBy(url => url)
            .ToList();

        var hashInput = string.Join("|", allSearchParams);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return ValueTask.FromResult(Convert.ToBase64String(hashBytes));
    }

    private IReadOnlyList<SearchParamJsonNode> BuildSearchParameters(List<Search.Models.SearchParameterInfo> searchParams)
    {
        var result = new List<SearchParamJsonNode>();

        foreach (var sp in searchParams.Where(p => p.IsSupported))
        {
            result.Add(new SearchParamJsonNode
            {
                Name = sp.Code,
                Definition = sp.Url?.ToString() ?? string.Empty,
                Type = MapSearchParamType(sp.Type),
                Documentation = sp.Description,
            });
        }

        return result;
    }

    private static SearchParamType MapSearchParamType(IgnixaSearchParamType type)
    {
        return type switch
        {
            IgnixaSearchParamType.Number => SearchParamType.Number,
            IgnixaSearchParamType.Date => SearchParamType.Date,
            IgnixaSearchParamType.String => SearchParamType.String,
            IgnixaSearchParamType.Token => SearchParamType.Token,
            IgnixaSearchParamType.Reference => SearchParamType.Reference,
            IgnixaSearchParamType.Composite => SearchParamType.Composite,
            IgnixaSearchParamType.Quantity => SearchParamType.Quantity,
            IgnixaSearchParamType.Uri => SearchParamType.Uri,
            IgnixaSearchParamType.Special => SearchParamType.Special,
            _ => SearchParamType.String,
        };
    }
}
