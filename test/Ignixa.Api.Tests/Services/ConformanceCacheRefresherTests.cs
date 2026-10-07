using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Services;

public class ConformanceCacheRefresherTests
{
    [Fact]
    public async Task GivenRecoverableTenantConfigurationFailure_WhenRefreshed_ThenItThrowsTheDedicatedRefreshException()
    {
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetAllTenantsAsync(CancellationToken.None)
            .Returns(ValueTask.FromException<IReadOnlyList<TenantConfiguration>>(
                new IOException("Configuration store unavailable.")));
        var refresher = new ConformanceCacheRefresher(null!, null!, tenantStore, null!, null!);
        using var state = new ConformanceState();

        var exception = await Should.ThrowAsync<ConformanceConsumerRefreshException>(() =>
            refresher.BuildSnapshotAsync(state.CreateSnapshot(), 0, CancellationToken.None));

        exception.InnerException.ShouldBeOfType<IOException>();
    }

    [Fact]
    public async Task GivenCancellation_WhenRefreshed_ThenItPropagatesTheCancellation()
    {
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetAllTenantsAsync(CancellationToken.None)
            .Returns(ValueTask.FromException<IReadOnlyList<TenantConfiguration>>(
                new OperationCanceledException("Independent cancellation.")));
        var refresher = new ConformanceCacheRefresher(null!, null!, tenantStore, null!, null!);
        using var state = new ConformanceState();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            refresher.BuildSnapshotAsync(state.CreateSnapshot(), 0, CancellationToken.None));
    }
}
