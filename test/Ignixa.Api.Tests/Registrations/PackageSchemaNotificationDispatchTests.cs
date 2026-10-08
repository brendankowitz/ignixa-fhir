using Autofac;
using Ignixa.Api.Registrations;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

public class PackageSchemaNotificationDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenActualApplicationRegistration_WhenPublishingConcretePackageEvent_ThenRefreshesSnapshotBeforeInvalidatingCapabilities(
        bool unload)
    {
        var registry = Substitute.For<ICompositeSchemaProviderRegistry>();
        var capabilities = Substitute.For<ICapabilityCacheInvalidator>();
        using var state = new ConformanceState();
        var refresher = new RecordingCacheRefresher(capabilities);
        var invalidatedAfterPublication = false;
        capabilities.When(service => service.InvalidateForTenantAsync(1, Arg.Any<CancellationToken>()))
            .Do(_ => invalidatedAfterPublication = refresher.PublishedGenerations.Count == 1);
        using var refreshPublisher = new ConformanceRefreshPublisher(
            state,
            refresher,
            NullLogger<ConformanceRefreshPublisher>.Instance);
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(new TenantConfiguration
        {
            TenantId = 1, DisplayName = "Package events", FhirVersion = "4.0.1",
            Storage = new TenantStorageConfiguration { Type = "FileSystem" }
        });
        var builder = new ContainerBuilder();
        builder.RegisterApplicationServices(new ConfigurationBuilder().Build());
        builder.RegisterInstance(registry).As<ICompositeSchemaProviderRegistry>();
        builder.RegisterInstance(capabilities).As<ICapabilityCacheInvalidator>();
        builder.RegisterInstance(refreshPublisher);
        builder.RegisterInstance(tenants).As<ITenantConfigurationStore>();
        builder.RegisterInstance(Substitute.For<IFhirVersionContext>()).As<IFhirVersionContext>();
        builder.RegisterInstance(new SqlServerSearchIndexCacheRegistry(
            Substitute.For<ISqlExecutionService>(), NullLoggerFactory.Instance));
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
        using var container = builder.Build();
        var mediator = container.Resolve<IMediator>();

        if (unload)
        {
            await mediator.PublishAsync(new PackageUnloadedEvent("local.ignixa.sqlonfhir", "2.1.0", 1, DateTimeOffset.UtcNow));
        }
        else
        {
            await mediator.PublishAsync(new PackageLoadedEvent("local.ignixa.sqlonfhir", "2.1.0", 1, DateTimeOffset.UtcNow));
        }

        refresher.BuiltGenerations.ShouldBe([0]);
        refresher.PublishedGenerations.ShouldBe([0]);
        await registry.DidNotReceiveWithAnyArgs()
            .InvalidateCacheForPackageAsync(default!, default, default);
        await capabilities.Received().InvalidateForTenantAsync(1, Arg.Any<CancellationToken>());
        invalidatedAfterPublication.ShouldBeTrue();
    }

    private sealed class RecordingCacheRefresher : IConformanceCacheRefresher
    {
        private readonly ICapabilityCacheInvalidator _capabilityCacheInvalidator;

        public RecordingCacheRefresher(ICapabilityCacheInvalidator capabilityCacheInvalidator)
        {
            _capabilityCacheInvalidator = capabilityCacheInvalidator;
        }

        public List<long> BuiltGenerations { get; } = [];
        public List<long> PublishedGenerations { get; } = [];

        public Task<IConformanceConsumerSnapshot> BuildSnapshotAsync(
            ConformanceStateSnapshot stateSnapshot,
            long generation,
            CancellationToken cancellationToken)
        {
            BuiltGenerations.Add(generation);
            return Task.FromResult<IConformanceConsumerSnapshot>(new Snapshot(generation));
        }

        public void PublishSnapshot(IConformanceConsumerSnapshot snapshot) =>
            PublishedGenerations.Add(snapshot.Generation);

        public ValueTask InvalidatePublishedSnapshotCachesAsync(
            IConformanceConsumerSnapshot snapshot,
            CancellationToken cancellationToken) =>
            _capabilityCacheInvalidator.InvalidateForTenantAsync(1, cancellationToken);

        private sealed record Snapshot(long Generation) : IConformanceConsumerSnapshot;
    }
}
