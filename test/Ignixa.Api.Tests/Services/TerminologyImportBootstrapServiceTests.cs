using Ignixa.Api.Services;
using Ignixa.Application.Events.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Services;

/// <summary>
/// The startup scan that picks up terminology resources loaded before auto-import was switched on, or left
/// non-terminal by a previous run. It reads through <see cref="IPackageResourceRepository"/> now rather than
/// LINQ-querying a <c>FhirDbContext</c>, which is what this pins.
/// </summary>
public class TerminologyImportBootstrapServiceTests
{
    private static readonly TimeSpan NoStartupDelay = TimeSpan.Zero;

    private static (TerminologyImportBootstrapService Service, IMediator Mediator) CreateService(
        IPackageResourceRepository packageResources)
    {
        var mediator = Substitute.For<IMediator>();

        var services = new ServiceCollection();
        services.AddSingleton(packageResources);
        services.AddSingleton(mediator);

        var service = new TerminologyImportBootstrapService(
            services.BuildServiceProvider(),
            NullLogger<TerminologyImportBootstrapService>.Instance,
            NoStartupDelay);

        return (service, mediator);
    }

    private static IPackageResourceRepository RepositoryReturning(params PendingTerminologyImport[] pending)
    {
        var packageResources = Substitute.For<IPackageResourceRepository>();
        packageResources
            .ListPendingTerminologyImportsAsync(null, null, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PendingTerminologyImport>>(_ => pending);

        return packageResources;
    }

    [Fact]
    public async Task GivenPendingTerminologyResources_WhenTheServiceRuns_ThenAnImportIsTriggeredPerPackageVersion()
    {
        var packageResources = RepositoryReturning(
            new PendingTerminologyImport("hl7.fhir.us.core", "5.0.1", [1L, 2L]),
            new PendingTerminologyImport("hl7.fhir.us.core", "6.1.0", [3L]));

        var (service, mediator) = CreateService(packageResources);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        // One orchestration per package version, carrying that version's own resource ids -- not one event
        // for everything found.
        await mediator.Received(1).PublishAsync(
            Arg.Is<TerminologyImportTriggeredEvent>(e =>
                e.PackageId == "hl7.fhir.us.core"
                && e.PackageVersion == "5.0.1"
                && e.PackageResourceIds.Count == 2),
            Arg.Any<CancellationToken>());

        await mediator.Received(1).PublishAsync(
            Arg.Is<TerminologyImportTriggeredEvent>(e =>
                e.PackageVersion == "6.1.0" && e.PackageResourceIds.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNothingPending_WhenTheServiceRuns_ThenNoImportIsTriggered()
    {
        var (service, mediator) = CreateService(RepositoryReturning());

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        await mediator.DidNotReceiveWithAnyArgs().PublishAsync(
            Arg.Any<TerminologyImportTriggeredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenOnePackageFailsToPublish_WhenTheServiceRuns_ThenTheOthersAreStillTriggered()
    {
        // A per-package catch, not one around the loop: a single bad package must not strand the rest.
        var packageResources = RepositoryReturning(
            new PendingTerminologyImport("bad.package", "1.0.0", [1L]),
            new PendingTerminologyImport("good.package", "1.0.0", [2L]));

        var (service, mediator) = CreateService(packageResources);

        mediator
            .When(m => m.PublishAsync(
                Arg.Is<TerminologyImportTriggeredEvent>(e => e.PackageId == "bad.package"),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("orchestration unavailable"));

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        await mediator.Received(1).PublishAsync(
            Arg.Is<TerminologyImportTriggeredEvent>(e => e.PackageId == "good.package"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenTheRepositoryThrows_WhenTheServiceRuns_ThenStartupIsNotBroughtDown()
    {
        // This runs as a hosted service; an escaping exception would take the host with it, and a failed
        // terminology scan is not worth refusing to serve requests over.
        var packageResources = Substitute.For<IPackageResourceRepository>();
        packageResources
            .ListPendingTerminologyImportsAsync(null, null, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PendingTerminologyImport>>(_ => throw new InvalidOperationException("database unavailable"));

        var (service, mediator) = CreateService(packageResources);

        await service.StartAsync(CancellationToken.None);
        await Should.NotThrowAsync(() => service.ExecuteTask!);

        await mediator.DidNotReceiveWithAnyArgs().PublishAsync(
            Arg.Any<TerminologyImportTriggeredEvent>(), Arg.Any<CancellationToken>());
    }
}
