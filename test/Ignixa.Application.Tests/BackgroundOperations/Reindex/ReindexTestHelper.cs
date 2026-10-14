using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Infrastructure;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

internal static class ReindexTestHelper
{
    public const string Canonical = "http://example.org/SearchParameter/patient-custom";

    public static readonly DateTimeOffset Now = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);

    /// <summary>The reindex target for the parameter <see cref="Activation"/> activates as <c>Patient.custom</c>.</summary>
    public static ReindexParameterDefinition Target(
        string canonical = Canonical,
        long activationEventId = 1,
        string? overridesCanonical = null,
        string? fhirVersion = null) =>
        new(canonical, "custom", "Patient", 17, activationEventId, ["Patient"], overridesCanonical, fhirVersion);

    public static SourceEvent Activation(
        string canonical = Canonical,
        long eventId = 1,
        string resourceType = "Patient",
        int searchParamId = 17,
        OverrideInfo? overrides = null,
        string? fhirVersion = null) => new(
        eventId,
        "search",
        nameof(SearchParameterActivated),
        new SearchParameterActivated(
            canonical, "custom", resourceType, $"{resourceType}.id", SearchParamType.String, "example@1.0.0", overrides,
            searchParamId, null, null, null, null, fhirVersion),
        DateTimeOffset.UtcNow);

    public static ReindexJobDefinition CreateJobDefinition(
        IReadOnlyList<ReindexParameterDefinition>? searchParameters = null,
        long targetEventId = 0) => new()
    {
        TargetEventId = targetEventId,
        TenantIds = [1],
        ResourceTypes = ["Patient"],
        SearchParameters = searchParameters ?? [],
        MaximumNumberOfResourcesPerQuery = 10_000,
        MaximumNumberOfResourcesPerWrite = 1_000,
        MaximumConcurrency = 4,
        QueryDelayIntervalInMilliseconds = 0,
        Trigger = "Manual"
    };

    /// <summary>A reindex job row last observed at <paramref name="observed"/>, ended then when terminal.</summary>
    public static BackgroundJob<ReindexJobDefinition> Job(
        string status,
        string jobId = "job",
        ReindexJobDefinition? definition = null,
        DateTimeOffset? observed = null) => new()
    {
        JobId = jobId,
        OrchestrationInstanceId = jobId,
        JobType = (int)BackgroundJobType.Reindex,
        Status = status,
        Definition = definition ?? CreateJobDefinition(),
        CreateDate = observed ?? Now,
        HeartbeatDate = observed ?? Now,
        EndDate = status is "Completed" or "Failed" or "Cancelled" ? observed ?? Now : null
    };

    /// <summary>An in-memory job store whose tenant store lists <paramref name="activeTenants"/>.</summary>
    public static InMemoryBackgroundJobRepository<ReindexJobDefinition> JobRepository(
        params TenantConfiguration[] activeTenants) =>
        JobRepository(TenantStore(activeTenants));

    public static InMemoryBackgroundJobRepository<ReindexJobDefinition> JobRepository(ITenantConfigurationStore tenants) =>
        new(tenants, NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);

    public static ITenantConfigurationStore TenantStore(params TenantConfiguration[] activeTenants)
    {
        var store = Substitute.For<ITenantConfigurationStore>();
        store.Mode.Returns(TenantMode.Isolated);
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns(activeTenants);
        return store;
    }

    public static TaskContext ActivityContext() => new(new OrchestrationInstance { InstanceId = "job" });

    public static ReindexOrchestrationInput CreateOrchestrationInput(
        TimeSpan? barrierDelay = null,
        IReadOnlyList<int>? tenantIds = null) =>
        new("job", 42, barrierDelay ?? TimeSpan.Zero, tenantIds ?? [1], ["Patient"], [], ReindexJobParameters.Create(),
            DrainWarningAfter: TimeSpan.FromMinutes(5), StaleJobTimeout: TimeSpan.FromMinutes(30), ContinueAsNewThreshold: 2_000);

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

    public static TenantConfiguration Tenant(int tenantId, string? storageType = null, string fhirVersion = "4.0") => new()
    {
        TenantId = tenantId,
        DisplayName = $"Tenant {tenantId}",
        FhirVersion = fhirVersion,
        IsActive = true,
        Storage = storageType is null ? new() : new() { Type = storageType }
    };
}
