// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IO;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Hands out SQL Server-backed repositories and search services for a tenant, replacing
/// <c>Ignixa.DataLayer.SqlEntityFramework.SqlEntityFrameworkRepositoryFactory</c> — which had already
/// delegated every construction to <see cref="SqlServerRepositoryFactory"/> and existed only to hold the
/// per-tenant state below, plus a <c>DbContextOptions</c> nothing on the production path read.
/// <para>
/// <b>What the per-tenant entry is for.</b> Neither
/// <see cref="SqlServerTenantInitializer.InitializeAsync"/> nor
/// <see cref="ManagedIdentityConnectionStringValidator.Validate"/> memoises anything downstream:
/// initialization re-runs schema deploy and unconditionally re-syncs the search-parameter catalog, and the
/// validator re-parses the connection string. Running either per <see cref="GetRepositoryAsync"/> would
/// make every request pay for both. The entry is a <see cref="Lazy{T}"/> over the initialization task so
/// concurrent first-callers await one run rather than racing, and a failed run is evicted so the next
/// caller retries instead of inheriting a permanently faulted task — matching
/// <see cref="SqlServerSearchIndexCacheRegistry"/>, and matching the EF factory, which never cached a
/// failed construction either.
/// </para>
/// <para>
/// <b>Why this sits beside <see cref="SqlServerRepositoryFactory"/> rather than absorbing it.</b> That
/// class is a stateless composition root with several consumers that have no tenant configuration store to
/// go through — the differential harness and the reference-data cache registry construct components from a
/// bare <see cref="ISqlExecutionService"/>. Folding it in would force those callers through tenant
/// resolution they neither have nor want. This type's job is the orthogonal one: resolve the tenant, guard
/// its credentials, initialize its database once, and cache what that produced.
/// </para>
/// </summary>
public sealed class SqlServerTenantServiceFactory : IFhirRepositoryFactory, ISearchServiceFactory
{
    private readonly ITenantConfigurationStore _tenantStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly SqlServerTenantInitializer _tenantInitializer;
    private readonly ManagedIdentityConnectionStringValidator _managedIdentityValidator;
    private readonly ISqlExecutionService _sqlExecutionService;
    private readonly ILogger<SqlServerTenantServiceFactory> _logger;

    private readonly ConcurrentDictionary<int, Lazy<Task<TenantServices>>> _tenantServices = new();
    private readonly ConcurrentDictionary<FhirVersion, DefinitionManagers> _definitionManagers = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerTenantServiceFactory"/> class.
    /// </summary>
    /// <param name="tenantStore">The tenant configuration store.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="memoryStreamManager">Recyclable memory stream manager backing resource compression.</param>
    /// <param name="tenantInitializer">Deploys/upgrades the tenant's schema, seeds its search-parameter catalog and preloads its reference data, in that order, before any repository is handed out.</param>
    /// <param name="managedIdentityValidator">Rejects password-bearing connection strings in Production.</param>
    /// <param name="sqlExecutionService">Tenant-scoped raw ADO.NET execution service backing both the write and read paths.</param>
    public SqlServerTenantServiceFactory(
        ITenantConfigurationStore tenantStore,
        ILoggerFactory loggerFactory,
        RecyclableMemoryStreamManager memoryStreamManager,
        SqlServerTenantInitializer tenantInitializer,
        ManagedIdentityConnectionStringValidator managedIdentityValidator,
        ISqlExecutionService sqlExecutionService)
    {
        ArgumentNullException.ThrowIfNull(tenantStore);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(memoryStreamManager);
        ArgumentNullException.ThrowIfNull(tenantInitializer);
        ArgumentNullException.ThrowIfNull(managedIdentityValidator);
        ArgumentNullException.ThrowIfNull(sqlExecutionService);

        _tenantStore = tenantStore;
        _loggerFactory = loggerFactory;
        _memoryStreamManager = memoryStreamManager;
        _tenantInitializer = tenantInitializer;
        _managedIdentityValidator = managedIdentityValidator;
        _sqlExecutionService = sqlExecutionService;
        _logger = loggerFactory.CreateLogger<SqlServerTenantServiceFactory>();
    }

    /// <summary>
    /// Gets the number of tenants whose initialization has been started or completed. Exposed for tests and
    /// diagnostics.
    /// </summary>
    public int InitializedTenantCount => _tenantServices.Count;

    /// <inheritdoc/>
    // CA1725 requires the parameter name to match IFhirRepositoryFactory's declaration, which is "ct".
    public async Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default)
    {
        var services = await GetOrInitializeTenantAsync(tenantId, ct);

        return SqlServerRepositoryFactory.CreateRepository(
            _sqlExecutionService,
            tenantId,
            services.ReferenceDataCache,
            _memoryStreamManager,
            _loggerFactory);
    }

    /// <inheritdoc/>
    // CA1725 requires the parameter name to match ISearchServiceFactory's declaration, which is "ct".
    public async Task<ISearchService> GetSearchServiceAsync(int tenantId, CancellationToken ct = default)
    {
        var services = await GetOrInitializeTenantAsync(tenantId, ct);

        return SqlServerRepositoryFactory.CreateSearchService(
            _sqlExecutionService,
            tenantId,
            services.ReferenceDataCache,
            services.Definitions.CompartmentManager,
            services.Definitions.ParameterManager,
            _memoryStreamManager,
            _loggerFactory);
    }

    private async Task<TenantServices> GetOrInitializeTenantAsync(int tenantId, CancellationToken cancellationToken)
    {
        // Before the lookup, because the wait below cannot be relied on to do it. Task.WaitAsync(token)
        // returns the task unchanged when it is ALREADY COMPLETE -- the runtime checks IsCompleted before it
        // checks the token -- and on the warm path (steady state, after a tenant's first request) it always
        // is. Every read and every write for every tenant arrives here, so without this a cancelled caller
        // was handed its services and kept working under load-shedding. Note the contrast with
        // SemaphoreSlim.WaitAsync(token), which does observe the token even when it can be satisfied
        // immediately; that is why the _dbLock sites elsewhere in this assembly need no equivalent. Matches
        // SqlServerSearchIndexCacheRegistry.GetOrCreateAsync, which has the same Lazy<Task> shape.
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _tenantServices.GetOrAdd(
            tenantId,
            id => new Lazy<Task<TenantServices>>(
                // CancellationToken.None: the result is shared by every subsequent request for this tenant,
                // so one caller's cancellation must not abandon a half-deployed schema for all of them.
                // While that initialization is still running, the caller's own token does release it from
                // the wait below; once it has completed, only the guard above observes cancellation.
                () => InitializeTenantAsync(id, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await entry.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            // Evict only when the shared initialization itself failed, so the next request retries rather
            // than inheriting a permanently faulted task. A caller that merely abandoned the wait leaves a
            // still-running initialization in place for everyone else.
            if (entry.Value.IsCompleted && !entry.Value.IsCompletedSuccessfully)
            {
                _tenantServices.TryRemove(new KeyValuePair<int, Lazy<Task<TenantServices>>>(tenantId, entry));
            }

            throw;
        }
    }

    private async Task<TenantServices> InitializeTenantAsync(int tenantId, CancellationToken cancellationToken)
    {
        var tenantConfig = await _tenantStore.GetTenantConfigurationAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist");

        if (!tenantConfig.IsActive)
        {
            throw new InvalidOperationException($"Tenant {tenantId} is not active");
        }

        var connectionString = await TenantConnectionStringResolver.ResolveAsync(
            _tenantStore, tenantId, cancellationToken);

        // Kept even though SqlExecutionService now runs the same guard on every connection it opens, because
        // this one runs EARLIER: tenant initialization below deploys the schema through SchemaDeployer, which
        // opens its own DacFx connection from the raw connection string and never goes through
        // ISqlExecutionService. Without this call a Production tenant configured with a password would have
        // its schema deployed over that password before the first query rejected it.
        // SqlServerTenantServiceFactoryValidationTests pins the ordering.
        _managedIdentityValidator.Validate(connectionString, tenantId);

        var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        var definitions = GetOrCreateDefinitionManagers(fhirVersion);

        _logger.LogInformation(
            "Initializing SQL Server services for tenant {TenantId} ({DisplayName}), FHIR {FhirVersion}",
            tenantId,
            tenantConfig.DisplayName,
            fhirVersion);

        var referenceDataCache = await _tenantInitializer.InitializeAsync(
            tenantId, definitions.ParameterManager, cancellationToken);

        _logger.LogInformation("SQL Server services initialized for tenant {TenantId}", tenantId);

        return new TenantServices(referenceDataCache, definitions);
    }

    private DefinitionManagers GetOrCreateDefinitionManagers(FhirVersion fhirVersion)
    {
        return _definitionManagers.GetOrAdd(fhirVersion, version => new DefinitionManagers(
            new CompartmentDefinitionManager(version),
            new SearchParameterDefinitionManager(
                version.GetSchemaProvider(),
                _loggerFactory.CreateLogger<SearchParameterDefinitionManager>())));
    }

    private sealed record DefinitionManagers(
        ICompartmentDefinitionManager CompartmentManager,
        ISearchParameterDefinitionManager ParameterManager);

    private sealed record TenantServices(
        SqlServerSearchIndexReferenceDataCache ReferenceDataCache,
        DefinitionManagers Definitions);
}
