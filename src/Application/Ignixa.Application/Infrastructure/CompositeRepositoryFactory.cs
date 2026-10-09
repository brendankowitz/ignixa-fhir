// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Composite repository factory that routes to the appropriate storage provider
/// based on tenant configuration (FileSystem, SqlEntityFramework, etc.).
/// Multi-tenancy: Each tenant can use a different storage backend.
/// </summary>
public class CompositeRepositoryFactory : IFhirRepositoryFactory
{
    private static readonly Dictionary<string, ProviderType> ProviderTypes = new(StringComparer.Ordinal)
    {
        ["FileSystem"] = ProviderType.FileSystem,
        ["SqlEntityFramework"] = ProviderType.SqlEntityFramework,
        ["SqlServer"] = ProviderType.SqlEntityFramework
    };

    private readonly ITenantConfigurationStore _tenantStore;
    private readonly IFhirRepositoryFactory _fileSystemFactory;
    private readonly IFhirRepositoryFactory _sqlEfFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeRepositoryFactory"/> class.
    /// </summary>
    /// <param name="tenantStore">The tenant configuration store.</param>
    /// <param name="fileSystemFactory">Factory for FileSystem storage.</param>
    /// <param name="sqlEfFactory">Factory for SQL EF storage.</param>
    public CompositeRepositoryFactory(
        ITenantConfigurationStore tenantStore,
        IFhirRepositoryFactory fileSystemFactory,
        IFhirRepositoryFactory sqlEfFactory)
    {
        _tenantStore = tenantStore ?? throw new ArgumentNullException(nameof(tenantStore));
        _fileSystemFactory = fileSystemFactory ?? throw new ArgumentNullException(nameof(fileSystemFactory));
        _sqlEfFactory = sqlEfFactory ?? throw new ArgumentNullException(nameof(sqlEfFactory));
    }

    /// <inheritdoc/>
    public async Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default)
    {
        var tenantConfig = await _tenantStore.GetTenantConfigurationAsync(tenantId, ct);

        if (tenantConfig == null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} does not exist");
        }

        return ResolveProviderType(tenantConfig.Storage.Type) switch
        {
            ProviderType.FileSystem => await _fileSystemFactory.GetRepositoryAsync(tenantId, ct),
            ProviderType.SqlEntityFramework => await _sqlEfFactory.GetRepositoryAsync(tenantId, ct),
            _ => throw new InvalidOperationException("Unrecognized provider type")
        };
    }

    /// <inheritdoc/>
    public bool SupportsReindex(TenantConfiguration tenantConfiguration)
    {
        ArgumentNullException.ThrowIfNull(tenantConfiguration);

        return ResolveProviderType(tenantConfiguration.Storage.Type) == ProviderType.SqlEntityFramework;
    }

    private static ProviderType ResolveProviderType(string storageType)
    {
        if (ProviderTypes.TryGetValue(storageType, out var providerType))
        {
            return providerType;
        }

        throw new NotSupportedException($"Storage type '{storageType}' is not supported");
    }

    private enum ProviderType
    {
        FileSystem,
        SqlEntityFramework
    }
}
