using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using NSubstitute;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

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

    public static ReindexOrchestrationInput CreateOrchestrationInput(
        string jobId,
        long targetEventId,
        TimeSpan barrierDelay,
        IReadOnlyList<int> tenantIds) =>
        new(
            jobId,
            targetEventId,
            barrierDelay,
            tenantIds,
            ["Patient"],
            [],
            ReindexJobParameters.Create());

    /// <summary>
    /// A repository factory whose tenant store lists <paramref name="tenants"/>; by default one SQL Server
    /// tenant, which makes reindex available.
    /// </summary>
    public static CompositeRepositoryFactory CreateRepositoryFactory(params TenantConfiguration[] tenants) =>
        new(
            TenantStore(tenants.Length == 0 ? [Tenant(1, "SqlServer")] : tenants),
            Substitute.For<IFhirRepositoryFactory>(),
            Substitute.For<IFhirRepositoryFactory>(),
            Substitute.For<IReindexStoreFactory>());

    public static TenantConfiguration Tenant(int tenantId, string storageType) => new()
    {
        TenantId = tenantId,
        DisplayName = $"Tenant {tenantId}",
        FhirVersion = "4.0",
        Storage = new TenantStorageConfiguration { Type = storageType }
    };

    private static ITenantConfigurationStore TenantStore(TenantConfiguration[] tenants)
    {
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns(tenants);
        return store;
    }
}
