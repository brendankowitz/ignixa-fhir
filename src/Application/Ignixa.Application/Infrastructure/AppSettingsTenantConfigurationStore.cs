// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Loads tenant configuration from appsettings.json.
/// Configurations are loaded from the "Tenants:Configurations" section.
/// Mode is loaded from "Tenants:Mode" section (Isolated or Distributed).
/// </summary>
public class AppSettingsTenantConfigurationStore : ITenantConfigurationStore
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AppSettingsTenantConfigurationStore> _logger;
    private readonly Lazy<TenantConfiguration[]> _tenants;
    private readonly Lazy<TenantMode> _mode;
    private readonly Lazy<IReadOnlyDictionary<string, TenantConfiguration>> _hostIndex;

    public AppSettingsTenantConfigurationStore(
        IConfiguration configuration,
        ILogger<AppSettingsTenantConfigurationStore> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenants = new Lazy<TenantConfiguration[]>(LoadTenants);
        _hostIndex = new Lazy<IReadOnlyDictionary<string, TenantConfiguration>>(BuildHostIndex);
        _mode = new Lazy<TenantMode>(LoadMode);
    }

    /// <summary>
    /// Gets the system-wide tenant mode (Isolated or Distributed).
    /// </summary>
    public TenantMode Mode => _mode.Value;

    private TenantMode LoadMode()
    {
        var modeString = _configuration["Tenants:Mode"];

        if (string.IsNullOrEmpty(modeString))
        {
            _logger.LogInformation("Tenants:Mode not configured, defaulting to Isolated mode");
            return TenantMode.Isolated;
        }

        if (Enum.TryParse<TenantMode>(modeString, ignoreCase: true, out var mode))
        {
            if (mode == TenantMode.Distributed)
            {
                _logger.LogWarning(
                    "Distributed mode is not yet supported. " +
                    "Distributed mode is reserved for future implementation (Phase 20.2+). " +
                    "The system will load but Distributed features will not work.");
            }

            _logger.LogInformation("Tenant mode configured as: {Mode}", mode);
            return mode;
        }

        _logger.LogWarning(
            "Invalid Tenants:Mode value '{Mode}', defaulting to Isolated",
            modeString);
        return TenantMode.Isolated;
    }

    private TenantConfiguration[] LoadTenants()
    {
        var tenantList = _configuration.GetSection("Tenants:Configurations")
            .Get<List<TenantConfiguration>>() ?? new List<TenantConfiguration>();

        // Validate: TenantId should match array index for efficient O(1) lookup
        for (int i = 0; i < tenantList.Count; i++)
        {
            if (tenantList[i].TenantId != i)
            {
                _logger.LogWarning(
                    "Tenant configuration warning: TenantId {TenantId} at index {Index}. " +
                    "For optimal performance, TenantId should match array index.",
                    tenantList[i].TenantId,
                    i);
            }
        }

        _logger.LogInformation("Loaded {Count} tenant configurations in {Mode} mode",
            tenantList.Count,
            Mode);

        return tenantList.ToArray();
    }

    private IReadOnlyDictionary<string, TenantConfiguration> BuildHostIndex()
    {
        var index = new Dictionary<string, TenantConfiguration>(StringComparer.OrdinalIgnoreCase);

        foreach (var tenant in _tenants.Value)
        {
            if (tenant.IsSystemPartition || !tenant.IsActive)
            {
                continue;
            }

            foreach (var host in tenant.Hostnames)
            {
                var normalized = host.Trim();

                // A malformed hostname (scheme, port, path, bad label) is never indexed, matching
                // FhirServiceBaseUriResolver's admission rule for the same shapes. Indexing it here while
                // the resolver refuses to recognize it would make the host resolve a tenant on inbound
                // routing yet never match on the way back out -- a silent dead route. Startup validation
                // (TenantHostnameValidator, via SearchServicesRegistration.ValidateTenantHostnames) is where
                // the operator-facing diagnostic for this lives; there is no logger here by design.
                if (!TenantHostnameValidator.IsValidHostname(normalized))
                {
                    continue;
                }

                if (index.TryGetValue(normalized, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Hostname '{normalized}' is claimed by both tenant {existing.TenantId} and tenant {tenant.TenantId}. A hostname must resolve exactly one tenant.");
                }

                index[normalized] = tenant;
            }
        }

        return index;
    }

    public ValueTask<TenantConfiguration?> ResolveByHostAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return _hostIndex.Value.TryGetValue(host.Trim(), out var tenant)
            ? ValueTask.FromResult<TenantConfiguration?>(tenant)
            : ValueTask.FromResult<TenantConfiguration?>(null);
    }

    public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(
        int tenantId,
        CancellationToken ct = default)
    {
        var tenants = _tenants.Value;

        // Try O(1) array access if TenantId matches index
        if (tenantId >= 0 && tenantId < tenants.Length && tenants[tenantId].TenantId == tenantId)
        {
            var config = tenants[tenantId];
            return ValueTask.FromResult<TenantConfiguration?>(config.IsActive ? config : null);
        }

        // Fallback: search for tenant ID (handles mismatched indices)
        var tenant = tenants.FirstOrDefault(t => t.TenantId == tenantId);
        return ValueTask.FromResult<TenantConfiguration?>(tenant?.IsActive == true ? tenant : null);
    }

    public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(
        CancellationToken ct = default)
    {
        // Filter out system partitions (IsSystemPartition = true)
        // System partitions are used internally for transaction IDs and system operations
        // and should not be exposed via tenant enumeration APIs (ADR-2523 Phase 20)
        var activeTenants = _tenants.Value
            .Where(t => t.IsActive && !t.IsSystemPartition)
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<TenantConfiguration>>(activeTenants);
    }
}
