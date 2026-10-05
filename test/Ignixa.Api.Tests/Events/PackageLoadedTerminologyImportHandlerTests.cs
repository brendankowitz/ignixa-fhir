using Ignixa.Api.Events;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Events.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Events;

/// <summary>
/// The ported terminology import trigger, which reads through <see cref="IPackageResourceRepository"/>
/// instead of the tenant-scoped <c>FhirDbContext</c> the EF handler opened.
/// </summary>
public class PackageLoadedTerminologyImportHandlerTests
{
    private static readonly PackageLoadedEvent Loaded =
        new("hl7.fhir.us.core", "6.1.0", 1, DateTimeOffset.UnixEpoch);

    private static PackageLoadedTerminologyImportHandler CreateHandler(
        IPackageResourceRepository packageResources, IMediator mediator) =>
        new(packageResources, mediator, NullLogger<PackageLoadedTerminologyImportHandler>.Instance);

    [Fact]
    public async Task GivenThePackageHasPendingTerminologyResources_WhenHandled_ThenAnImportEventIsPublishedForThem()
    {
        var packageResources = Substitute.For<IPackageResourceRepository>();
        packageResources
            .ListPendingTerminologyImportsAsync("hl7.fhir.us.core", "6.1.0", Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PendingTerminologyImport>>(_ =>
                [new PendingTerminologyImport("hl7.fhir.us.core", "6.1.0", [7L, 8L])]);

        var mediator = Substitute.For<IMediator>();

        await CreateHandler(packageResources, mediator).HandleAsync(Loaded, CancellationToken.None);

        // The event's tenant is carried through for the orchestration's request context even though it does
        // not select the resources -- dbo.PackageResource has no tenant column.
        await mediator.Received(1).PublishAsync(
            Arg.Is<TerminologyImportTriggeredEvent>(e =>
                e.TenantId == 1
                && e.PackageId == "hl7.fhir.us.core"
                && e.PackageVersion == "6.1.0"
                && e.PackageResourceIds.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenThePackageHasNoPendingTerminologyResources_WhenHandled_ThenNothingIsPublished()
    {
        var packageResources = Substitute.For<IPackageResourceRepository>();
        packageResources
            .ListPendingTerminologyImportsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PendingTerminologyImport>>(_ => []);

        var mediator = Substitute.For<IMediator>();

        await CreateHandler(packageResources, mediator).HandleAsync(Loaded, CancellationToken.None);

        await mediator.DidNotReceiveWithAnyArgs().PublishAsync(
            Arg.Any<TerminologyImportTriggeredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenTheLookupFails_WhenHandled_ThenThePackageLoadIsNotFailed()
    {
        // Deliberately swallowed, unlike PackageLoadedSearchParameterSyncHandler which rethrows. The package
        // is already stored by the time this runs and the bootstrap scan re-offers anything still pending,
        // so failing the load here would discard a successful install over a recoverable step.
        var packageResources = Substitute.For<IPackageResourceRepository>();
        packageResources
            .ListPendingTerminologyImportsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PendingTerminologyImport>>(_ => throw new InvalidOperationException("database unavailable"));

        var handler = CreateHandler(packageResources, Substitute.For<IMediator>());

        await Should.NotThrowAsync(() => handler.HandleAsync(Loaded, CancellationToken.None));
    }
}
