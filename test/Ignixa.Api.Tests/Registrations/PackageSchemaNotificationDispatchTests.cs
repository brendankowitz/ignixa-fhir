using Autofac;
using Ignixa.Api.Registrations;
using Ignixa.Abstractions;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
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
        var tenant = new TenantConfiguration
        {
            TenantId = 1, DisplayName = "Package events", FhirVersion = "4.0.1",
            Storage = new TenantStorageConfiguration { Type = "FileSystem" }
        };
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(tenant);
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>([tenant]));
        var versions = Substitute.For<IFhirVersionContext>();
        var builtGenerations = new List<long>();
        var publishedGenerations = new List<long>();
        var definitions = Substitute.For<ISearchParameterDefinitionManager>();
        versions.CreateConformanceDefinitionsSnapshot(
                Arg.Any<FhirVersion>(), Arg.Any<int>(), Arg.Any<ConformanceStateSnapshot>(), Arg.Any<long>())
            .Returns(call =>
            {
                builtGenerations.Add(call.ArgAt<long>(3));
                return new ConformanceDefinitionsSnapshot(
                    definitions,
                    definitions,
                    new DefinitionsHandle(
                        Substitute.For<ISearchIndexer>(),
                        Substitute.For<IFhirSchemaProvider>(),
                        call.ArgAt<long>(3)));
            });
        versions.When(context => context.PublishConformanceDefinitionsSnapshot(
                Arg.Any<FhirVersion>(), Arg.Any<int>(), Arg.Any<ConformanceDefinitionsSnapshot>()))
            .Do(call => publishedGenerations.Add(call.Arg<ConformanceDefinitionsSnapshot>().Generation));
        var invalidatedAfterPublication = false;
        capabilities.When(service => service.InvalidateForTenantAsync(1, Arg.Any<CancellationToken>()))
            .Do(_ => invalidatedAfterPublication = publishedGenerations.Count == 1);
        using var conformanceRefresher = TestConformanceRefresher.Create(
            state,
            tenants: tenants,
            versions: versions,
            capabilities: capabilities);
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

        builtGenerations.ShouldBe([0]);
        publishedGenerations.ShouldBe([0]);
        await registry.DidNotReceiveWithAnyArgs()
            .InvalidateCacheForPackageAsync(default!, default, default);
        await capabilities.Received().InvalidateForTenantAsync(1, Arg.Any<CancellationToken>());
        invalidatedAfterPublication.ShouldBeTrue();
    }
}
