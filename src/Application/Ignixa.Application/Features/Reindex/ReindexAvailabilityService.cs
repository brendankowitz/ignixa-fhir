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
    private readonly object _availabilityLock = new();
    private Task<ReindexAvailability>? _availabilityTask;

    public Task<ReindexAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_availabilityLock)
        {
            var availabilityTask = _availabilityTask;
            if (availabilityTask is null)
            {
                var completionSource = new TaskCompletionSource<ReindexAvailability>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                availabilityTask = completionSource.Task;
                _availabilityTask = availabilityTask;
                _ = CompleteAvailabilityProbeAsync(completionSource);
            }

            return availabilityTask.WaitAsync(cancellationToken);
        }
    }

    private async Task CompleteAvailabilityProbeAsync(
        TaskCompletionSource<ReindexAvailability> completionSource)
    {
        try
        {
            completionSource.SetResult(await ProbeAvailabilityAsync());
        }
        catch (Exception exception)
        {
            lock (_availabilityLock)
            {
                if (ReferenceEquals(_availabilityTask, completionSource.Task))
                {
                    _availabilityTask = null;
                }
            }

            completionSource.SetException(exception);
        }
    }

    private async Task<ReindexAvailability> ProbeAvailabilityAsync()
    {
        if (!_options.Enabled)
        {
            return ReindexAvailability.Disabled;
        }

        var tenants = await _tenantConfigurationStore.GetAllTenantsAsync(CancellationToken.None);
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
