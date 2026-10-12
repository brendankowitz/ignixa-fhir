// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Composite repository factory that routes to the appropriate storage provider
/// based on tenant configuration (FileSystem, SqlEntityFramework, etc.).
/// Multi-tenancy: Each tenant can use a different storage backend.
/// Only the SQL Server provider can reindex; this is the one place that decides which providers can.
/// </summary>
public class CompositeRepositoryFactory : IFhirRepositoryFactory, IReindexStoreFactory
{
    private readonly ITenantConfigurationStore _tenantStore;
    private readonly IFhirRepositoryFactory _fileSystemFactory;
    private readonly IFhirRepositoryFactory _sqlEfFactory;
    private readonly IReindexStoreFactory _sqlReindexStoreFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeRepositoryFactory"/> class.
    /// </summary>
    /// <param name="tenantStore">The tenant configuration store.</param>
    /// <param name="fileSystemFactory">Factory for FileSystem storage.</param>
    /// <param name="sqlEfFactory">Factory for SQL EF storage.</param>
    /// <param name="sqlReindexStoreFactory">Reindex store factory for SQL storage.</param>
    public CompositeRepositoryFactory(
        ITenantConfigurationStore tenantStore,
        IFhirRepositoryFactory fileSystemFactory,
        IFhirRepositoryFactory sqlEfFactory,
        IReindexStoreFactory sqlReindexStoreFactory)
    {
        _tenantStore = tenantStore ?? throw new ArgumentNullException(nameof(tenantStore));
        _fileSystemFactory = fileSystemFactory ?? throw new ArgumentNullException(nameof(fileSystemFactory));
        _sqlEfFactory = sqlEfFactory ?? throw new ArgumentNullException(nameof(sqlEfFactory));
        _sqlReindexStoreFactory = sqlReindexStoreFactory ?? throw new ArgumentNullException(nameof(sqlReindexStoreFactory));
    }

    /// <inheritdoc/>
    public async Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default)
    {
        var tenantConfig = await _tenantStore.GetTenantConfigurationAsync(tenantId, ct);

        if (tenantConfig == null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} does not exist");
        }

        // Route to appropriate factory based on storage type
        return tenantConfig.Storage.Type switch
        {
            "FileSystem" => await _fileSystemFactory.GetRepositoryAsync(tenantId, ct),
            "SqlEntityFramework" or "SqlServer" => await _sqlEfFactory.GetRepositoryAsync(tenantId, ct),
            _ => throw new NotSupportedException($"Storage type '{tenantConfig.Storage.Type}' is not supported")
        };
    }

    /// <inheritdoc/>
    public async Task<IReindexStore> GetReindexStoreAsync(int tenantId, CancellationToken cancellationToken)
    {
        var tenantConfig = await _tenantStore.GetTenantConfigurationAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist");

        return IsSql(tenantConfig.Storage.Type)
            ? await _sqlReindexStoreFactory.GetReindexStoreAsync(tenantId, cancellationToken)
            : throw new NotSupportedException(
                $"The storage provider '{tenantConfig.Storage.Type}' of tenant {tenantId} does not support $reindex.");
    }

    /// <summary>
    /// Finds the lowest id of an active, non-system tenant whose storage provider cannot reindex. The tenant
    /// store is read on every call, so a tenant added at runtime is reflected at once.
    /// </summary>
    /// <returns>The tenant id, or <see langword="null"/> when every active tenant can reindex.</returns>
    /// <exception cref="NotSupportedException">An active tenant has an unrecognized storage type.</exception>
    public async Task<int?> FindTenantWithoutReindexSupportAsync(CancellationToken cancellationToken)
    {
        var tenants = await _tenantStore.GetAllTenantsAsync(cancellationToken);

        // Every active tenant is classified, so a misconfigured storage type fails even when an earlier
        // tenant is already unsupported.
        var unsupportedTenantIds = tenants
            .Where(tenant => tenant.IsActive && tenant.TenantId != SystemConstants.SystemPartitionId)
            .Where(tenant => !IsSql(tenant.Storage.Type))
            .Select(tenant => tenant.TenantId)
            .ToList();

        return unsupportedTenantIds.Count == 0 ? null : unsupportedTenantIds.Min();
    }

    /// <exception cref="NotSupportedException">The storage type is not recognized.</exception>
    private static bool IsSql(string storageType) => storageType switch
    {
        "SqlEntityFramework" or "SqlServer" => true,
        "FileSystem" => false,
        _ => throw new NotSupportedException($"Storage type '{storageType}' is not supported")
    };
}
