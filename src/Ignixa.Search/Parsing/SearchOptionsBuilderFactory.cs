// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Ignixa.Domain;
using Ignixa.Specification;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Infrastructure;
using Ignixa.Serialization;

namespace Ignixa.Search.Parsing;

/// <summary>
/// Factory that creates and caches version-aware SearchOptionsBuilder instances.
/// Thread-safe singleton that creates one builder per (tenant, FHIR version) pair.
/// Phase 1: Single-tenant mode (uses TenantContext.Default).
/// Phase 2+: Multi-tenant mode with custom search parameters per tenant.
/// </summary>
public sealed class SearchOptionsBuilderFactory : ISearchOptionsBuilderFactory, IDisposable
{
    private readonly IFhirVersionContext _versionContext;
    private readonly ConcurrentDictionary<(TenantContext Tenant, FhirSpecification Version), ISearchOptionsBuilder> _builderCache = new();
    private readonly SemaphoreSlim _creationLock = new(1, 1);
    private bool _disposed;

    public SearchOptionsBuilderFactory(IFhirVersionContext versionContext)
    {
        EnsureArg.IsNotNull(versionContext, nameof(versionContext));
        _versionContext = versionContext;
    }

    /// <inheritdoc/>
    public ISearchOptionsBuilder Create(FhirSpecification fhirVersion)
    {
        // Phase 1: Single-tenant mode - always use default tenant
        // Phase 2+: Extract tenant from HttpContext and create tenant-specific builders
        return CreateForTenant(TenantContext.Default, fhirVersion);
    }

    /// <summary>
    /// Creates a SearchOptionsBuilder for the specified tenant and FHIR version.
    /// Internal method for future multi-tenant support.
    /// </summary>
    /// <param name="tenant">The tenant context.</param>
    /// <param name="fhirVersion">The FHIR version.</param>
    private ISearchOptionsBuilder CreateForTenant(
        TenantContext tenant,
        FhirSpecification fhirVersion)
    {
        var cacheKey = (tenant, fhirVersion);

        // Fast path: check cache
        if (_builderCache.TryGetValue(cacheKey, out var cachedBuilder))
        {
            return cachedBuilder;
        }

        // Slow path: create new builder with version-specific dependencies
        _creationLock.Wait();
        try
        {
            // Double-check after acquiring lock
            if (_builderCache.TryGetValue(cacheKey, out cachedBuilder))
            {
                return cachedBuilder;
            }

            // Get version-specific components from context (cached and reused)
            var schemaProvider = _versionContext.GetBaseSchemaProvider(fhirVersion);
            var searchParamDefinitionManager = _versionContext.GetSearchParameterDefinitionManager(fhirVersion);

            // Create resolver delegate for SearchParameterDefinitionManager
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver resolver =
                () => searchParamDefinitionManager;

            // Create version-specific ReferenceSearchValueParser (builds regex patterns per version)
            var referenceParser = new ReferenceSearchValueParser(schemaProvider);

            // Create version-specific SearchParameterExpressionParser (needs both parser and provider)
            var searchParamExpressionParser = new SearchParameterExpressionParser(referenceParser, schemaProvider);

            // Create version-specific ExpressionParser
            var expressionParser = new ExpressionParser(
                resolver,
                searchParamExpressionParser,
                schemaProvider);

            // Create version-specific SearchOptionsBuilder
            var builder = new SearchOptionsBuilder(expressionParser, searchParamDefinitionManager);

            // Cache and return
            // Phase 2+: This cache will hold separate builders for (tenant, version) pairs
            // allowing custom search parameters per tenant
            _builderCache.TryAdd(cacheKey, builder);
            return builder;
        }
        finally
        {
            _creationLock.Release();
        }
    }

    /// <summary>
    /// Disposes the SemaphoreSlim used for thread synchronization.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _creationLock?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
