// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
using Ignixa.Application.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Executes synthetic HttpContext objects through the ASP.NET Core pipeline.
/// Matches requests to registered endpoints and invokes their handlers.
/// Uses ASP.NET Core endpoint routing similar to microsoft/fhir-server BundleRouter.
/// </summary>
public class AspNetCorePipelineExecutor : IPipelineExecutor
{
    private readonly EndpointDataSource _endpointDataSource;
    private readonly IEnumerable<MatcherPolicy> _matcherPolicies;
    private readonly EndpointSelector _endpointSelector;
    private readonly TemplateBinderFactory _templateBinderFactory;

    public AspNetCorePipelineExecutor(
        EndpointDataSource endpointDataSource,
        IEnumerable<MatcherPolicy> matcherPolicies,
        EndpointSelector endpointSelector,
        TemplateBinderFactory templateBinderFactory)
    {
        _endpointDataSource = endpointDataSource ?? throw new ArgumentNullException(nameof(endpointDataSource));
        _matcherPolicies = matcherPolicies ?? throw new ArgumentNullException(nameof(matcherPolicies));
        _endpointSelector = endpointSelector ?? throw new ArgumentNullException(nameof(endpointSelector));
        _templateBinderFactory = templateBinderFactory ?? throw new ArgumentNullException(nameof(templateBinderFactory));
    }

    /// <summary>
    /// Executes a request through the ASP.NET Core pipeline by matching and invoking the appropriate endpoint.
    /// Uses proper route template matching with TemplateMatcher, constraint processing, and policy application.
    /// Based on microsoft/fhir-server BundleRouter implementation.
    /// </summary>
    public async Task ExecuteAsync(HttpContext context)
    {
        var routeCandidates = new Dictionary<RouteEndpoint, RouteValueDictionary>();
        IEnumerable<RouteEndpoint> endpoints = _endpointDataSource.Endpoints.OfType<RouteEndpoint>();
        PathString path = context.Request.Path;

        // Phase 1: Find all endpoints that match the path template
        foreach (RouteEndpoint endpoint in endpoints)
        {
            var routeValues = new RouteValueDictionary();
            var routeDefaults = new RouteValueDictionary(endpoint.RoutePattern.Defaults);

            RoutePattern pattern = endpoint.RoutePattern;
            TemplateBinder templateBinder = _templateBinderFactory.Create(pattern);

            var templateMatcher = new TemplateMatcher(new RouteTemplate(pattern), routeDefaults);

            // Pattern match
            if (!templateMatcher.TryMatch(path, routeValues))
            {
                continue;
            }

            // Eliminate routes that don't match constraints
            if (!templateBinder.TryProcessConstraints(context, routeValues, out _, out _))
            {
                continue;
            }

            routeCandidates.Add(endpoint, routeValues);
        }

        if (routeCandidates.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Phase 2: Apply policies (HTTP method, consumes, etc.) to narrow down candidates
        var candidateSet = new CandidateSet(
            routeCandidates.Select(x => x.Key).Cast<Endpoint>().ToArray(),
            routeCandidates.Select(x => x.Value).ToArray(),
            Enumerable.Repeat(1, routeCandidates.Count).ToArray());

        // Policies apply filters / matches on attributes such as HTTP verbs, Consumes, etc.
        foreach (IEndpointSelectorPolicy policy in _matcherPolicies
                     .OrderBy(x => x.Order)
                     .OfType<IEndpointSelectorPolicy>())
        {
            await policy.ApplyAsync(context, candidateSet);
        }

        // Phase 3: Select the best matching endpoint
        await _endpointSelector.SelectAsync(context, candidateSet);

        Endpoint? selectedEndpoint = context.GetEndpoint();

        // Execute the selected endpoint
        if (selectedEndpoint is RouteEndpoint routeEndpoint && routeEndpoint.RequestDelegate != null)
        {
            // Route values are already set by the endpoint selector
            await routeEndpoint.RequestDelegate(context);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
