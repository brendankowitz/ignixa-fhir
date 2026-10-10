using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using NSubstitute;

namespace Ignixa.Api.Tests.Services;

internal static class ReindexTestHelper
{
    public static ReindexJobDefinition CreateJobDefinition() => new()
    {
        TargetEventId = 0,
        TenantIds = [1],
        ResourceTypes = [],
        SearchParameters = [],
        MaximumNumberOfResourcesPerQuery = 10_000,
        MaximumNumberOfResourcesPerWrite = 1_000,
        MaximumConcurrency = 4,
        QueryDelayIntervalInMilliseconds = 0,
        Trigger = "Manual"
    };

    /// <summary>
    /// A repository factory whose tenant store lists <paramref name="tenants"/>; by default one SQL Server
    /// tenant, which makes reindex available.
    /// </summary>
    public static CompositeRepositoryFactory CreateRepositoryFactory(params TenantConfiguration[] tenants)
    {
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(tenants.Length == 0 ? [Tenant(1, "SqlServer")] : tenants);
        return new CompositeRepositoryFactory(
            store,
            Substitute.For<IFhirRepositoryFactory>(),
            Substitute.For<IFhirRepositoryFactory>(),
            Substitute.For<IReindexStoreFactory>());
    }

    public static TenantConfiguration Tenant(int tenantId, string storageType) => new()
    {
        TenantId = tenantId,
        DisplayName = $"Tenant {tenantId}",
        FhirVersion = "4.0",
        Storage = new TenantStorageConfiguration { Type = storageType }
    };
}
