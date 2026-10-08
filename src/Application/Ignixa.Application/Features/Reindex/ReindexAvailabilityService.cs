using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Reindex;

public sealed class ReindexAvailabilityService(
    IOptions<ReindexOptions> options,
    ITenantConfigurationStore tenantConfigurationStore,
    IFhirRepositoryFactory repositoryFactory) : IReindexAvailability
{
    private readonly ReindexOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ITenantConfigurationStore _tenantConfigurationStore =
        tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
    private readonly IFhirRepositoryFactory _repositoryFactory =
        repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));

    public async Task<ReindexAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return ReindexAvailability.Disabled;
        }

        var tenants = await _tenantConfigurationStore.GetAllTenantsAsync(cancellationToken);
        foreach (var tenant in tenants
            .Where(tenant => tenant.IsActive && tenant.TenantId != SystemConstants.SystemPartitionId)
            .OrderBy(tenant => tenant.TenantId))
        {
            var repository = await _repositoryFactory.GetRepositoryAsync(
                tenant.TenantId,
                cancellationToken);
            if (repository is not IReindexStore)
            {
                return new ReindexAvailability(
                    ReindexAvailabilityStatus.Unsupported,
                    tenant.TenantId);
            }
        }

        return ReindexAvailability.Available;
    }
}
