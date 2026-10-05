using Autofac;
using Ignixa.Api.Registrations;
using Ignixa.Application.Events.Package;
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

namespace Ignixa.Api.Tests.Registrations;

public class PackageSchemaNotificationDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenActualApplicationRegistration_WhenPublishingConcretePackageEvent_ThenInvalidatesSchemasAndCapabilities(
        bool unload)
    {
        var registry = Substitute.For<ICompositeSchemaProviderRegistry>();
        var capabilities = Substitute.For<ICapabilityCacheInvalidator>();
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

        await registry.Received(1).InvalidateCacheForPackageAsync(
            "local.ignixa.sqlonfhir", 1, Arg.Any<CancellationToken>());
        await capabilities.Received().InvalidateForTenantAsync(1, Arg.Any<CancellationToken>());
    }
}
