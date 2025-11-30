// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Include/RevInclude capability segment.
/// Populates searchInclude and searchRevInclude for each resource type based on reference search parameters.
/// Also documents support for chained parameters.
/// Changes when search parameters are added/removed or reference targets change.
/// </summary>
public class IncludeRevIncludeCapabilitySegment : ICapabilitySegment
{
    private readonly IFhirVersionContext _versionContext;
    private readonly ILogger<IncludeRevIncludeCapabilitySegment> _logger;

    public IncludeRevIncludeCapabilitySegment(
        IFhirVersionContext versionContext,
        ILogger<IncludeRevIncludeCapabilitySegment> logger)
    {
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string SegmentKey => "include-revinclude";

    public int Priority => 40; // Execute after search parameters (30)

    public ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying include/revinclude capability segment for {FhirVersion}", context.FhirVersion);

        // Get manager and schema provider for this FHIR version
        var manager = _versionContext.GetSearchParameterDefinitionManager(context.FhirVersion);
        var schemaProvider = _versionContext.GetBaseSchemaProvider(context.FhirVersion);

        if (statement.Rest == null || statement.Rest.Count == 0)
        {
            _logger.LogWarning("No REST component found in capability statement - includes will not be added");
            return ValueTask.CompletedTask;
        }

        var restComponent = statement.Rest[0];
        if (restComponent.Resource == null)
        {
            _logger.LogWarning("No resources found in REST component - includes will not be added");
            return ValueTask.CompletedTask;
        }

        // Build a map of all resource types for quick lookup
        var resourceMap = restComponent.Resource.ToDictionary(r => r.Type ?? string.Empty);

        int totalIncludes = 0;
        int totalRevIncludes = 0;

        // Populate include/revinclude for each resource
        foreach (var resource in restComponent.Resource)
        {
            if (string.IsNullOrEmpty(resource.Type))
            {
                continue;
            }

            if (!manager.TryGetSearchParameters(resource.Type, out var searchParamsEnumerable))
            {
                continue;
            }

            var searchParams = searchParamsEnumerable.ToList();

            // Build searchInclude list from reference parameters
            var includeList = BuildSearchIncludes(resource.Type, searchParams);
            if (includeList.Count > 0)
            {
                resource.SearchInclude = includeList;
                totalIncludes += includeList.Count;
            }

            // Build searchRevInclude list - find all reference parameters that target this resource
            var revIncludeList = BuildSearchRevIncludes(resource.Type, manager, resourceMap.Keys);
            if (revIncludeList.Count > 0)
            {
                resource.SearchRevInclude = revIncludeList;
                totalRevIncludes += revIncludeList.Count;
            }
        }

        _logger.LogDebug("Added {IncludeCount} _include and {RevIncludeCount} _revinclude entries across {ResourceCount} resources",
            totalIncludes, totalRevIncludes, restComponent.Resource.Count);

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Hash is based on all reference search parameters and their targets
        var manager = _versionContext.GetSearchParameterDefinitionManager(context.FhirVersion);

        var referenceParams = manager.AllSearchParameters
            .Where(sp => sp.IsSupported && sp.Type == SearchParamType.Reference)
            .OrderBy(sp => sp.Code)
            .ThenBy(sp => string.Join(",", sp.BaseResourceTypes ?? Array.Empty<string>()))
            .Select(sp => $"{sp.Code}:{string.Join(",", sp.BaseResourceTypes ?? Array.Empty<string>())}:{string.Join(",", sp.TargetResourceTypes ?? Array.Empty<string>())}")
            .ToList();

        var hashInput = string.Join("|", referenceParams);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return ValueTask.FromResult(Convert.ToBase64String(hashBytes));
    }

    /// <summary>
    /// Build searchInclude list for a resource type.
    /// Format: "ResourceType:searchParameter" for each reference parameter.
    /// Also adds "ResourceType:*" to support wildcard includes.
    /// </summary>
    private List<string> BuildSearchIncludes(
        string resourceType,
        List<SearchParameterInfo> searchParams)
    {
        var includes = new List<string>();

        // Add specific reference parameters
        var referenceParams = searchParams
            .Where(sp => sp.IsSupported && sp.Type == SearchParamType.Reference)
            .OrderBy(sp => sp.Code)
            .ToList();

        foreach (var refParam in referenceParams)
        {
            includes.Add($"{resourceType}:{refParam.Code}");
        }

        // Add wildcard support if there are any reference parameters
        if (includes.Count > 0)
        {
            includes.Add($"{resourceType}:*");
        }

        return includes;
    }

    /// <summary>
    /// Build searchRevInclude list for a resource type.
    /// Format: "SourceResourceType:searchParameter" for each reference parameter that targets this resource.
    /// </summary>
    private List<string> BuildSearchRevIncludes(
        string targetResourceType,
        ISearchParameterDefinitionManager manager,
        IEnumerable<string> allResourceTypes)
    {
        var revIncludes = new List<string>();

        // For each resource type, find reference parameters that target this resource
        foreach (var sourceResourceType in allResourceTypes)
        {
            if (!manager.TryGetSearchParameters(sourceResourceType, out var searchParamsEnumerable))
            {
                continue;
            }

            var searchParams = searchParamsEnumerable;

            var referenceParams = searchParams
                .Where(sp => sp.IsSupported &&
                           sp.Type == SearchParamType.Reference &&
                           (sp.TargetResourceTypes == null ||
                            sp.TargetResourceTypes.Count == 0 ||
                            sp.TargetResourceTypes.Contains(targetResourceType)))
                .OrderBy(sp => sp.Code)
                .ToList();

            foreach (var refParam in referenceParams)
            {
                revIncludes.Add($"{sourceResourceType}:{refParam.Code}");
            }
        }

        return revIncludes.Distinct().OrderBy(x => x).ToList();
    }
}

