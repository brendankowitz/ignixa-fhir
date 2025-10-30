// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Extensions;
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
    private readonly ConcurrentDictionary<int, TenantServiceFactory> _factoryCache;
    private readonly ConcurrentDictionary<FhirSpecification, (CompartmentDefinitionManager CompartmentManager, SearchParameterDefinitionManager ParameterManager)> _definitionManagersCache;

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
    public SqlEntityFrameworkRepositoryFactory(
        ITenantConfigurationStore tenantStore,
        ILoggerFactory loggerFactory,
        RecyclableMemoryStreamManager memoryStreamManager)
    {
        _tenantStore = tenantStore ?? throw new ArgumentNullException(nameof(tenantStore));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _memoryStreamManager = memoryStreamManager ?? throw new ArgumentNullException(nameof(memoryStreamManager));
        _factoryCache = new ConcurrentDictionary<int, TenantServiceFactory>();
        _definitionManagersCache = new ConcurrentDictionary<FhirSpecification, (CompartmentDefinitionManager, SearchParameterDefinitionManager)>();
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

        // Prevent access to system partition (Partition 0)
        if (tenantConfig.IsSystemPartition || tenantId == SystemConstants.SystemPartitionId)
        {
            throw new InvalidOperationException($"Tenant {tenantId} is a system partition and cannot be accessed directly");
        }

        // Validate storage configuration
        if (tenantConfig.Storage.Type != "SqlEntityFramework" && tenantConfig.Storage.Type != "SqlServer")
        {
            throw new InvalidOperationException($"Tenant {tenantId} storage type '{tenantConfig.Storage.Type}' is not supported by SqlEntityFrameworkRepositoryFactory. Expected 'SqlEntityFramework' or 'SqlServer'");
        }

        if (string.IsNullOrEmpty(tenantConfig.Storage.ConnectionString))
        {
            throw new InvalidOperationException($"Tenant {tenantId} is missing a connection string in Storage.ConnectionString");
        }

        // SECURITY: Validate that connection string uses Managed Identity (Azure AD) authentication
        ValidateManagedIdentityAuthentication(tenantConfig.Storage.ConnectionString, tenantId);

        // Create factory and cache it
        var factory = _factoryCache.GetOrAdd(tenantId, _ => CreateServiceFactory(tenantId, tenantConfig));

        return factory;
    }

    /// <summary>
    /// Validates that the connection string uses Managed Identity (Azure AD) authentication.
    /// Throws an exception if local SQL authentication (username/password) is detected.
    /// </summary>
    /// <remarks>
    /// Production deployments should ONLY use Managed Identity authentication for security.
    /// Local SQL authentication (sa account, SQL logins with passwords) creates security risks:
    /// - Passwords must be stored/rotated
    /// - Cannot use Azure RBAC for access control
    /// - Violates principle of least privilege
    ///
    /// Expected connection string format:
    /// Server=tcp:servername.database.windows.net,1433;Database=FhirDatabase;Encrypt=true;TrustServerCertificate=false;Authentication=Active Directory Managed Identity;
    /// </remarks>
    private void ValidateManagedIdentityAuthentication(string connectionString, int tenantId)
    {
        var logger = _loggerFactory.CreateLogger<SqlEntityFrameworkRepositoryFactory>();

        // Check if connection string contains password-based authentication indicators
        bool hasPassword = connectionString.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
                           connectionString.Contains("pwd=", StringComparison.OrdinalIgnoreCase);

        if (hasPassword)
        {
            logger.LogError(
                "Tenant {TenantId} connection string contains local SQL authentication (User ID/Password). " +
                "Production deployments MUST use Managed Identity (Azure AD) authentication. " +
                "Expected: Authentication=Active Directory Managed Identity;",
                tenantId);
            throw new InvalidOperationException(
                $"Tenant {tenantId} connection string contains local SQL authentication (User ID/Password). " +
                "Production deployments MUST use Managed Identity (Azure AD) authentication. " +
                "Expected: Authentication=Active Directory Managed Identity;");
        }

        // Check if connection string explicitly uses Managed Identity
        bool usesManagedIdentity = connectionString.Contains("Authentication=Active Directory Managed Identity", StringComparison.OrdinalIgnoreCase);

        if (!usesManagedIdentity)
        {
            // Warning if auth method is not explicitly specified (might default to local auth)
            logger.LogWarning(
                "Tenant {TenantId} connection string does not explicitly specify ' Authentication=Active Directory Managed Identity'. " +
                "Verify this is intentional and that local SQL authentication is disabled on the server.",
                tenantId);
        }

        logger.LogInformation("Tenant {TenantId} validated for Managed Identity authentication", tenantId);
    }

    /// <summary>
    /// Gets or creates cached definition managers for the given FHIR specification.
    /// Managers are cached by version to avoid recreating them for multiple tenants using the same FHIR version.
    /// </summary>
    private (CompartmentDefinitionManager CompartmentManager, SearchParameterDefinitionManager ParameterManager) GetOrCreateDefinitionManagers(
        FhirSpecification fhirSpec,
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

    private TenantServiceFactory CreateServiceFactory(int tenantId, Domain.Models.TenantConfiguration tenantConfig)
    {
        var logger = _loggerFactory.CreateLogger<SqlEntityFrameworkRepositoryFactory>();
        logger.LogInformation("Creating service factory for tenant {TenantId} ({DisplayName})", tenantId, tenantConfig.DisplayName);

        // Create DbContext OPTIONS (thread-safe, can be cached)
        var optionsBuilder = new DbContextOptionsBuilder<FhirDbContext>();
        optionsBuilder.UseSqlServer(
            tenantConfig.Storage.ConnectionString,
            sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                sqlOptions.CommandTimeout(30);
            });

        // Enable sensitive data logging in development (optional)
        // optionsBuilder.EnableSensitiveDataLogging();

        var dbContextOptions = optionsBuilder.Options;

        // Create a TEMPORARY DbContext just for initialization (will be disposed)
        var initDbContext = new FhirDbContext(dbContextOptions);

        // CRITICAL: Apply pending migrations automatically on first access
        // This ensures TVP types and stored procedures are created
        // Also sets up Managed Identity database user (extracted from environment or configuration)
        string? managedIdentityName = null;
        try
        {
            logger.LogInformation("Ensuring database migrations are applied for tenant {TenantId}...", tenantId);

            // Attempt to extract Managed Identity name from connection string (User ID parameter)
            // If specified in connection string, use that for MI setup
            // Otherwise, the running process identity is used (Managed Identity of App Service)
            managedIdentityName = ExtractManagedIdentityNameFromConnectionString(tenantConfig.Storage.ConnectionString);

            var initializer = new DatabaseInitializer(
                initDbContext,
                _loggerFactory.CreateLogger<DatabaseInitializer>());

            // Initialize with optional MI setup (idempotent - safe to run multiple times)
            initializer.InitializeAsync(managedIdentityName).GetAwaiter().GetResult(); // Synchronous wait (factory is not async)
            logger.LogInformation("Database initialization completed for tenant {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to initialize database for tenant {TenantId}. Error: {Message}",
                tenantId,
                ex.Message);
            throw;
        }
        finally
        {
            // Dispose the temporary initialization DbContext
            initDbContext.Dispose();
        }

        // Convert FhirVersion string to FhirSpecification enum using extension method
        var fhirSpec = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);

        // Get appropriate IFhirSchemaProvider using extension method
        var schemaProvider = fhirSpec.GetSchemaProvider();

        // Get or create cached definition managers (reused across tenants with same FHIR version)
        var (compartmentManager, parameterManager) = GetOrCreateDefinitionManagers(fhirSpec, schemaProvider);

        // Create factory delegate for Repository (accepts DbContext parameter)
        Func<FhirDbContext, IFhirRepository> createRepository = (dbContext) =>
        {
            var compressor = new GzipResourceCompressor(_memoryStreamManager);

            var sqlMergeRepository = new SqlMergeRepository(
                dbContext,
                compressor,
                _loggerFactory.CreateLogger<SqlMergeRepository>());

            return new SqlEntityFrameworkRepository(
                dbContext,
                compressor,
                sqlMergeRepository,
                _loggerFactory.CreateLogger<SqlEntityFrameworkRepository>());
        };

        // Create factory delegate for SearchService (accepts DbContext and Repository parameters)
        Func<FhirDbContext, IFhirRepository, ISearchService> createSearchService = (dbContext, repository) =>
        {
            var compressor = new GzipResourceCompressor(_memoryStreamManager);
            var searchIndexCache = new SearchIndexReferenceDataCache(
                dbContext,
                _loggerFactory.CreateLogger<SearchIndexReferenceDataCache>());

            var parameterQueryGenerator = new Search.SearchParameterQueryGenerator(
                dbContext,
                searchIndexCache,
                _loggerFactory.CreateLogger<Search.SearchParameterQueryGenerator>());

            var chainedExpressionProcessor = new Search.ChainedExpressionProcessor(
                dbContext,
                searchIndexCache,
                parameterQueryGenerator,
                _loggerFactory.CreateLogger<Search.ChainedExpressionProcessor>());

            var compartmentQueryGenerator = new Search.CompartmentSearchQueryGenerator(
                dbContext,
                searchIndexCache,
                compartmentManager,
                parameterManager,
                _loggerFactory.CreateLogger<Search.CompartmentSearchQueryGenerator>());

            var queryBuilder = new Search.SearchExpressionQueryBuilder(
                dbContext,
                parameterQueryGenerator,
                chainedExpressionProcessor,
                compartmentQueryGenerator,
                _loggerFactory.CreateLogger<Search.SearchExpressionQueryBuilder>());

            var includeProcessor = new Search.IncludeProcessor(
                dbContext,
                searchIndexCache,
                compressor,
                _loggerFactory.CreateLogger<Search.IncludeProcessor>());

            var revIncludeProcessor = new Search.RevIncludeProcessor(
                dbContext,
                searchIndexCache,
                compressor,
                _loggerFactory.CreateLogger<Search.RevIncludeProcessor>());

            var iterateProcessor = new Search.IterateProcessor(
                includeProcessor,
                revIncludeProcessor,
                _loggerFactory.CreateLogger<Search.IterateProcessor>());

            return new Search.SqlEntityFrameworkSearchService(
                dbContext,
                repository,
                queryBuilder,
                includeProcessor,
                revIncludeProcessor,
                iterateProcessor,
                compressor,
                _loggerFactory.CreateLogger<Search.SqlEntityFrameworkSearchService>());
        };

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
