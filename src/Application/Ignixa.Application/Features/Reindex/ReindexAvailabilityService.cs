using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Reindex;

public sealed class ReindexAvailabilityService(
    IOptions<ReindexOptions> options,
    ITenantConfigurationStore tenantConfigurationStore,
    IReindexProviderCapabilities providerCapabilities) : IReindexAvailability
{
    private readonly ReindexOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ITenantConfigurationStore _tenantConfigurationStore =
        tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
    private readonly IReindexProviderCapabilities _providerCapabilities =
        providerCapabilities ?? throw new ArgumentNullException(nameof(providerCapabilities));

    public async Task<ReindexAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return ReindexAvailability.Disabled;
        }

        var tenants = await _tenantConfigurationStore.GetAllTenantsAsync(cancellationToken);
        int? unsupportedTenantId = null;
        foreach (var tenant in tenants
            .Where(tenant => tenant.IsActive && tenant.TenantId != SystemConstants.SystemPartitionId)
            .OrderBy(tenant => tenant.TenantId))
        {
            if (!_providerCapabilities.SupportsReindex(tenant) && unsupportedTenantId is null)
            {
                unsupportedTenantId = tenant.TenantId;
            }
        }

        return unsupportedTenantId is int tenantId
            ? new ReindexAvailability(ReindexAvailabilityStatus.Unsupported, tenantId)
            : ReindexAvailability.Available;
    }
}
