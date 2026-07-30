// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlServer;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Factory for creating tenant-specific SqlEntityFrameworkRepository instances.
/// Implements caching to provide O(1) repository lookup after first access.
/// Each tenant gets its own DbContext with a dedicated connection string.
/// Caches definition managers (CompartmentDefinitionManager, SearchParameterDefinitionManager) by FHIR version
/// to avoid recreating them for each tenant.
/// </summary>
public class SqlEntityFrameworkRepositoryFactory : IFhirRepositoryFactory, ISearchServiceFactory
{
    private readonly ITenantConfigurationStore _tenantStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly MultiTenantSearchIndexCache _multiTenantCache;
    private readonly SqlServerTenantInitializer _tenantInitializer;
    private readonly ManagedIdentityConnectionStringValidator _managedIdentityValidator;
    private readonly ISqlExecutionService _sqlExecutionService;
    private readonly ConcurrentDictionary<int, TenantServiceFactory> _factoryCache;
    private readonly ConcurrentDictionary<FhirVersion, (CompartmentDefinitionManager CompartmentManager, SearchParameterDefinitionManager ParameterManager)> _definitionManagersCache;

    /// <summary>
    /// Container for tenant-specific configuration and factory delegates.
    /// Does NOT cache DbContext instances - creates new instances per request instead.
    /// </summary>
    private class TenantServiceFactory
    {
        public required DbContextOptions<FhirDbContext> DbContextOptions { get; init; }
        public required Func<FhirDbContext, IFhirRepository> CreateRepository { get; init; }
        public required Func<FhirDbContext, IFhirRepository, ISearchService> CreateSearchService { get; init; }
        public required string? ManagedIdentityName { get; init; }
        public required bool IsInitialized { get; init; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlEntityFrameworkRepositoryFactory"/> class.
    /// </summary>
    /// <param name="tenantStore">The tenant configuration store.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="memoryStreamManager">The recyclable memory stream manager for efficient memory management.</param>
    /// <param name="multiTenantCache">Singleton multi-tenant cache for search index reference data.</param>
    /// <param name="tenantInitializer">Deploys/upgrades the tenant's schema, seeds its search-parameter catalog and preloads its reference data, in that order, before any repository is handed out.</param>
    /// <param name="managedIdentityValidator">Rejects password-bearing connection strings in Production.</param>
    /// <param name="sqlExecutionService">Tenant-scoped raw ADO.NET execution service backing the SqlServer write path (<see cref="SqlServerFhirRepository"/>).</param>
    public SqlEntityFrameworkRepositoryFactory(
        ITenantConfigurationStore tenantStore,
        ILoggerFactory loggerFactory,
        RecyclableMemoryStreamManager memoryStreamManager,
        MultiTenantSearchIndexCache multiTenantCache,
        SqlServerTenantInitializer tenantInitializer,
        ManagedIdentityConnectionStringValidator managedIdentityValidator,
        ISqlExecutionService sqlExecutionService)
    {
        _tenantStore = tenantStore ?? throw new ArgumentNullException(nameof(tenantStore));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _memoryStreamManager = memoryStreamManager ?? throw new ArgumentNullException(nameof(memoryStreamManager));
        _multiTenantCache = multiTenantCache ?? throw new ArgumentNullException(nameof(multiTenantCache));
        _tenantInitializer = tenantInitializer ?? throw new ArgumentNullException(nameof(tenantInitializer));
        _managedIdentityValidator = managedIdentityValidator ?? throw new ArgumentNullException(nameof(managedIdentityValidator));
        _sqlExecutionService = sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
        _factoryCache = new ConcurrentDictionary<int, TenantServiceFactory>();
        _definitionManagersCache = new ConcurrentDictionary<FhirVersion, (CompartmentDefinitionManager, SearchParameterDefinitionManager)>();
    }

    /// <inheritdoc/>
    public async Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default)
    {
        var factory = await GetOrCreateFactoryAsync(tenantId, ct);

        // Create a new DbContext for this request (thread-safe)
        // CA2000: DbContext disposal is responsibility of calling code (Repository will be disposed by DI container)
#pragma warning disable CA2000 // Dispose objects before losing scope
        var dbContext = new FhirDbContext(factory.DbContextOptions);
#pragma warning restore CA2000

        // Use the cached factory function to create the repository with the new DbContext
        return factory.CreateRepository(dbContext);
    }

    /// <inheritdoc/>
    public async Task<ISearchService> GetSearchServiceAsync(int tenantId, CancellationToken ct = default)
    {
        var factory = await GetOrCreateFactoryAsync(tenantId, ct);

        // Create a new DbContext for this request (thread-safe)
        // CA2000: DbContext disposal is responsibility of calling code (SearchService will be disposed by DI container)
#pragma warning disable CA2000 // Dispose objects before losing scope
        var dbContext = new FhirDbContext(factory.DbContextOptions);
#pragma warning restore CA2000

        // Create repository and search service with the new DbContext
        var repository = factory.CreateRepository(dbContext);
        return factory.CreateSearchService(dbContext, repository);
    }

    /// <summary>
    /// Gets a new FhirDbContext instance for the specified tenant.
    /// The caller is responsible for disposing the context.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new FhirDbContext instance.</returns>
    public async Task<FhirDbContext> GetDbContextAsync(int tenantId, CancellationToken ct = default)
    {
        var factory = await GetOrCreateFactoryAsync(tenantId, ct);

        // Create a new DbContext for this request (thread-safe)
        // CA2000: DbContext disposal is responsibility of calling code
#pragma warning disable CA2000 // Dispose objects before losing scope
        return new FhirDbContext(factory.DbContextOptions);
#pragma warning restore CA2000
    }

    private async Task<TenantServiceFactory> GetOrCreateFactoryAsync(int tenantId, CancellationToken ct)
    {
        // Check cache first
        if (_factoryCache.TryGetValue(tenantId, out var cachedFactory))
        {
            return cachedFactory;
        }

        // Get tenant configuration
        var tenantConfig = await _tenantStore.GetTenantConfigurationAsync(tenantId, ct);

        if (tenantConfig == null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} does not exist");
        }

        if (!tenantConfig.IsActive)
        {
            throw new InvalidOperationException($"Tenant {tenantId} is not active");
        }

        // Storage-type gate and the system partition's connection-string inheritance both live in
        // Ignixa.DataLayer.SqlServer now -- SqlExecutionService already needed the identical rules, and
        // two hand-synchronised copies of them is one copy too many.
        var connectionString = await SqlServerTenantConnectionResolver.ResolveConnectionStringAsync(
            _tenantStore, tenantId, ct);

        // SECURITY: Validate that connection string uses Managed Identity (Azure AD) authentication
        _managedIdentityValidator.Validate(connectionString, tenantId);

        // Create factory and cache it
        var factory = _factoryCache.GetOrAdd(tenantId, _ => CreateServiceFactory(tenantId, tenantConfig, connectionString));

        return factory;
    }

    /// <summary>
    /// Gets or creates cached definition managers for the given FHIR specification.
    /// Managers are cached by version to avoid recreating them for multiple tenants using the same FHIR version.
    /// </summary>
    private (CompartmentDefinitionManager CompartmentManager, SearchParameterDefinitionManager ParameterManager) GetOrCreateDefinitionManagers(
        FhirVersion fhirSpec,
        IFhirSchemaProvider schemaProvider)
    {
        return _definitionManagersCache.GetOrAdd(fhirSpec, _ =>
        {
            var compartmentManager = new CompartmentDefinitionManager(fhirSpec);
            var parameterManager = new SearchParameterDefinitionManager(
                schemaProvider,
                _loggerFactory.CreateLogger<SearchParameterDefinitionManager>());
            return (compartmentManager, parameterManager);
        });
    }

    private TenantServiceFactory CreateServiceFactory(int tenantId, Domain.Models.TenantConfiguration tenantConfig, string connectionString)
    {
        var logger = _loggerFactory.CreateLogger<SqlEntityFrameworkRepositoryFactory>();
        logger.LogInformation("Creating service factory for tenant {TenantId} ({DisplayName})", tenantId, tenantConfig.DisplayName);

        // Create DbContext OPTIONS (thread-safe, can be cached)
        var optionsBuilder = new DbContextOptionsBuilder<FhirDbContext>();
        optionsBuilder.UseSqlServer(
            connectionString,
            sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                sqlOptions.CommandTimeout(30);
            });

        // Enable sensitive data logging in development (optional)
        // optionsBuilder.EnableSensitiveDataLogging();

        var dbContextOptions = optionsBuilder.Options;

        // Attempt to extract Managed Identity name from connection string (User ID parameter)
        // If specified in connection string, use that for MI setup
        // Otherwise, the running process identity is used (Managed Identity of App Service)
        var managedIdentityName = ExtractManagedIdentityNameFromConnectionString(connectionString);

        // Convert FhirVersion string to FhirVersion enum using extension method
        var fhirSpec = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);

        // Get appropriate IFhirSchemaProvider using extension method
        var schemaProvider = fhirSpec.GetSchemaProvider();

        // Get or create cached definition managers (reused across tenants with same FHIR version).
        // Purely in-memory, so hoisting it above the database initialization below costs nothing and
        // lets the initializer be handed the parameter manager it seeds the catalog from.
        var (compartmentManager, parameterManager) = GetOrCreateDefinitionManagers(fhirSpec, schemaProvider);

        // Schema deploy -> schema upgrade -> search-parameter catalog seed -> reference-data preload,
        // in that order and exactly once per tenant, all of it now owned by
        // SqlServerTenantInitializer (Ignixa.DataLayer.SqlServer) rather than inlined here. Returns
        // the tenant's single shared reference-data cache -- the same instance the package-load sync
        // reaches, which is what stops the write path silently dropping index rows.
        var sqlServerSearchIndexCache = _tenantInitializer
            .InitializeAsync(tenantId, parameterManager, CancellationToken.None)
            .GetAwaiter().GetResult(); // Synchronous wait (factory is not async)

        // Create factory delegate for Repository (accepts DbContext parameter, retained for
        // GetSearchServiceAsync's benefit -- see createSearchService below -- but unused here since
        // SqlServerFhirRepository writes through ISqlExecutionService directly, not the DbContext).
        // CUTOVER (Phase D, Task 11): writes go through SqlServerFhirRepository, not
        // SqlEntityFrameworkRepository/SqlMergeRepository. Straight, unconditional swap -- no
        // feature flag (design doc §5). It receives whatever IFhirRepository it's handed purely
        // through the IFhirRepository interface, with no downcast, so it is unaffected by this swap.
        // Reads also cut over to SqlServerCompiledSearchService (createSearchService below, sub-project
        // 3 Task 14) -- SqlEntityFrameworkSearchService and its generator chain remain in the codebase,
        // untouched, as a rollback lever, but createSearchService no longer constructs them.
        Func<FhirDbContext, IFhirRepository> createRepository = (_) =>
            SqlServerRepositoryFactory.CreateRepository(
                _sqlExecutionService, tenantId, sqlServerSearchIndexCache, _memoryStreamManager, _loggerFactory);

        // Create factory delegate for SearchService (accepts DbContext and Repository parameters,
        // both retained for GetSearchServiceAsync's call-site shape but unused here since
        // SqlServerCompiledSearchService drives Ignixa.Search.Sql's compiler directly through
        // ISqlExecutionService, not the DbContext/IFhirRepository).
        // CUTOVER (search adapter design doc, Task 14): reads go through SqlServerCompiledSearchService,
        // not SqlEntityFrameworkSearchService. Straight, unconditional swap -- no feature flag, mirroring
        // createRepository's own cutover above. Search.SqlEntityFrameworkSearchService and its query
        // generators remain in the codebase untouched as the reference implementation and rollback
        // lever, but this closure no longer constructs them.
        Func<FhirDbContext, IFhirRepository, ISearchService> createSearchService = (_, _) =>
            SqlServerRepositoryFactory.CreateSearchService(
                _sqlExecutionService, tenantId, sqlServerSearchIndexCache, compartmentManager, parameterManager,
                _memoryStreamManager, _loggerFactory);

        logger.LogInformation("Successfully created service factory for tenant {TenantId}", tenantId);

        return new TenantServiceFactory
        {
            DbContextOptions = dbContextOptions,
            CreateRepository = createRepository,
            CreateSearchService = createSearchService,
            ManagedIdentityName = managedIdentityName,
            IsInitialized = true
        };
    }

    /// <summary>
    /// Gets the tenant-specific SearchIndexReferenceDataCache for syncing search parameters.
    /// Used by PackageLoadedEventHandler to sync package search parameters to database.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tenant-specific SearchIndexReferenceDataCache.</returns>
    public async Task<SearchIndexReferenceDataCache> GetSearchIndexReferenceCacheAsync(int tenantId, CancellationToken ct = default)
    {
        var factory = await GetOrCreateFactoryAsync(tenantId, ct);
        return _multiTenantCache.GetOrCreateCacheForTenant(tenantId, factory.DbContextOptions);
    }

    /// <summary>
    /// Extracts the Managed Identity name (Client ID or App Service name) from connection string.
    /// The connection string can optionally include "User ID=&lt;client-id-or-name&gt;" for explicit MI identification.
    /// If not specified in connection string, returns null (the running process identity is used).
    /// </summary>
    /// <remarks>
    /// Connection string formats:
    /// - With explicit Client ID: Server=...;User ID=fhir-prod-yourorg;Authentication=Active Directory Managed Identity;
    /// - Without Client ID: Server=...;Authentication=Active Directory Managed Identity; (uses running process identity)
    ///
    /// The "User ID" parameter can be:
    /// - Azure AD Client ID (GUID)
    /// - App Service name (e.g., 'fhir-prod-yourorg')
    /// - Service principal display name
    /// </remarks>
    /// <returns>The User ID if found in connection string, otherwise null (uses running identity).</returns>
    private string? ExtractManagedIdentityNameFromConnectionString(string? connectionString)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionString))
                return null;

            var logger = _loggerFactory.CreateLogger<SqlEntityFrameworkRepositoryFactory>();

            // Parse connection string for User ID parameter
            // Handle both "User ID=" and "UID=" formats
            var userId = ExtractConnectionStringValue(connectionString, "User ID") ??
                        ExtractConnectionStringValue(connectionString, "UID");

            if (!string.IsNullOrEmpty(userId))
            {
                logger.LogDebug("Extracted Managed Identity User ID from connection string: {UserId}", userId);
                return userId;
            }

            logger.LogDebug("No User ID found in connection string; will use running process identity");
            return null;
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<SqlEntityFrameworkRepositoryFactory>()
                .LogDebug(ex, "Failed to extract MI name from connection string; will use running process identity");
            return null;
        }
    }

    /// <summary>
    /// Extracts a value from a connection string by key (case-insensitive, handles both ; and ; separators).
    /// </summary>
    private string? ExtractConnectionStringValue(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString))
            return null;

        // Split by semicolon and look for key=value pairs
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var kvp = part.Split('=', 2);
            if (kvp.Length == 2 &&
                kvp[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp[1].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Clears all caches (factory delegates and definition managers). Useful for testing or when tenant configurations change.
    /// </summary>
    public void ClearCache()
    {
        _factoryCache.Clear();
        _definitionManagersCache.Clear();
    }

    /// <summary>
    /// Gets the current number of cached tenant service factories.
    /// </summary>
    public int CachedServicesCount => _factoryCache.Count;
}
