// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Domain;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Parsing;

namespace Ignixa.Application.Features.Search;

/// <summary>
/// Factory that creates and caches version-aware SearchOptionsBuilder instances.
/// Thread-safe singleton that creates one builder per (tenant, FHIR version) pair.
/// Phase 1: Single-tenant mode (uses TenantContext.Default).
/// Phase 2+: Multi-tenant mode with custom search parameters per tenant.
/// </summary>
public sealed class SearchOptionsBuilderFactory : ISearchOptionsBuilderFactory, IDisposable
{
    private readonly IFhirVersionContext _versionContext;
    private readonly ConcurrentDictionary<(TenantContext Tenant, FhirVersion Version), ISearchOptionsBuilder> _builderCache = new();
    private readonly ConcurrentDictionary<(TenantContext Tenant, FhirVersion Version, int? TenantId), ISearchOptionsBuilder> _tenantBuilderCache = new();
    private readonly SemaphoreSlim _creationLock = new(1, 1);
    private bool _disposed;

    public SearchOptionsBuilderFactory(IFhirVersionContext versionContext)
    {
        EnsureArg.IsNotNull(versionContext, nameof(versionContext));
        _versionContext = versionContext;
    }

    /// <inheritdoc/>
    public ISearchOptionsBuilder Create(FhirVersion fhirVersion)
    {
        // Uses base search parameter definitions only (no tenant-specific IG parameters)
        return CreateForTenant(TenantContext.Default, fhirVersion, tenantId: null);
    }

    /// <inheritdoc/>
    public ISearchOptionsBuilder Create(FhirVersion fhirVersion, int? tenantId)
    {
        // Uses tenant-specific search parameter definitions including IG parameters
        return CreateForTenant(TenantContext.Default, fhirVersion, tenantId);
    }

    /// <summary>
    /// Creates a SearchOptionsBuilder for the specified tenant and FHIR version.
    /// Internal method for multi-tenant support with IG-specific search parameters.
    /// </summary>
    /// <param name="tenant">The tenant context.</param>
    /// <param name="fhirVersion">The FHIR version.</param>
    /// <param name="tenantId">The tenant ID for IG-specific search parameters (null uses base definitions only).</param>
    private ISearchOptionsBuilder CreateForTenant(
        TenantContext tenant,
        FhirVersion fhirVersion,
        int? tenantId)
    {
        // Include tenantId in cache key to separate tenant-specific builders
        var cacheKey = (tenant, fhirVersion, tenantId);

        // Fast path: check cache (use TryGetValue on extension for tuple key)
        if (_builderCache.TryGetValue((tenant, fhirVersion), out var cachedBuilder) && !tenantId.HasValue)
        {
            return cachedBuilder;
        }

        // Check tenant-specific cache
        if (tenantId.HasValue && _tenantBuilderCache.TryGetValue(cacheKey, out var tenantCachedBuilder))
        {
            return tenantCachedBuilder;
        }

        // Slow path: create new builder with version-specific dependencies
        _creationLock.Wait();
        try
        {
            // Double-check after acquiring lock
            if (!tenantId.HasValue && _builderCache.TryGetValue((tenant, fhirVersion), out cachedBuilder))
            {
                return cachedBuilder;
            }

            if (tenantId.HasValue && _tenantBuilderCache.TryGetValue(cacheKey, out tenantCachedBuilder))
            {
                return tenantCachedBuilder;
            }

            // Get version-specific components from context (cached and reused)
            var schemaProvider = _versionContext.GetBaseSchemaProvider(fhirVersion);

            // CRITICAL: Use tenant-specific search parameter manager when tenantId is provided
            // This ensures query parsing uses the same search parameters as indexing (including US Core)
            var searchParamDefinitionManager = tenantId.HasValue
                ? _versionContext.GetSearchParameterDefinitionManager(fhirVersion, tenantId)
                : _versionContext.GetSearchParameterDefinitionManager(fhirVersion);

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

            // Cache and return - use appropriate cache based on tenant ID
            if (tenantId.HasValue)
            {
                _tenantBuilderCache.TryAdd(cacheKey, builder);
            }
            else
            {
                _builderCache.TryAdd((tenant, fhirVersion), builder);
            }

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
