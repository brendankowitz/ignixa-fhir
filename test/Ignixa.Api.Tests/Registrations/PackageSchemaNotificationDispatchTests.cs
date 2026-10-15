using Autofac;
using Ignixa.Api.Registrations;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Application.Tests;
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
        var effects = new List<string>();
        var capabilities = TestConformanceRefresher.RecordingCapabilities(effects);
        var versions = TestConformanceRefresher.RecordingVersions(effects);
        var tenant = new TenantConfiguration { TenantId = 1, DisplayName = "Package events", FhirVersion = "4.0.1", Storage = new() { Type = "FileSystem" } };
        var tenants = TestConformanceRefresher.Tenants(tenant);
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(tenant);
        using var state = new ConformanceState();
        using var conformanceRefresher = TestConformanceRefresher.Create(state, tenants: tenants, versions: versions, capabilities: capabilities);
        var builder = new ContainerBuilder();
        builder.RegisterApplicationServices(new ConfigurationBuilder().Build());
        builder.RegisterInstance(registry).As<ICompositeSchemaProviderRegistry>();
        builder.RegisterInstance(capabilities).As<ICapabilityCacheInvalidator>();
        builder.RegisterInstance(conformanceRefresher);
        builder.RegisterInstance(tenants).As<ITenantConfigurationStore>();
        builder.RegisterInstance(versions).As<IFhirVersionContext>();
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

        effects.Take(2).ShouldBe(["built 1@0", "published 1@0"]);
        effects.Skip(2).ShouldNotBeEmpty();
        effects.Skip(2).ShouldAllBe(effect => effect == "invalidated 1");
        await registry.DidNotReceiveWithAnyArgs().InvalidateCacheForPackageAsync(default!, default, default);
    }
}
