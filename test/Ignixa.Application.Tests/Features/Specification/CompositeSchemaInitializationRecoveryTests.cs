using Ignixa.Abstractions;
using Ignixa.Application.Features.Specification;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using PackageResourceProvider = Ignixa.PackageManagement.Infrastructure.PackageResourceProvider;

namespace Ignixa.Application.Tests.Features.Specification;

public class CompositeSchemaInitializationRecoveryTests
{
    [Fact]
    public async Task GivenTransientFailure_WhenSynchronousAdmissionIsRetried_ThenPreservesErrorAndRecovers()
    {
        var failure = new TimeoutException("Package SQL read timed out");
        var repository = TransientRepository(failure);
        var provider = CreateProvider(repository);

        Should.Throw<TimeoutException>(() => _ = provider.ResourceTypeNames).ShouldBeSameAs(failure);

        provider.ResourceTypeNames.ShouldContain("Patient");
        await repository.Received(2).GetAllStructureDefinitionsAsync("4.0.1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenTransientFailure_WhenAsyncInitializationIsRetried_ThenPreservesErrorAndRecovers()
    {
        var failure = new TimeoutException("Package SQL read timed out");
        var repository = TransientRepository(failure);
        var provider = CreateProvider(repository);

        (await Should.ThrowAsync<TimeoutException>(() => provider.InitializeAsync())).ShouldBeSameAs(failure);

        await provider.InitializeAsync();
        provider.ResourceTypeNames.ShouldContain("Patient");
        await repository.Received(2).GetAllStructureDefinitionsAsync("4.0.1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenOldFailedGenerationAfterClearCache_WhenFailureArrives_ThenKeepsSuccessfulReplacement()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRead = new TaskCompletionSource<IReadOnlyList<PackageResource>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetAllStructureDefinitionsAsync("4.0.1", Arg.Any<CancellationToken>()).Returns(
            _ =>
            {
                started.TrySetResult();
                return oldRead.Task;
            },
            _ => Task.FromResult<IReadOnlyList<PackageResource>>([]));
        var provider = CreateProvider(repository);
        var oldInitialization = provider.InitializeAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        provider.ClearCache();
        await provider.InitializeAsync();
        var failure = new TimeoutException("Old generation failed late");

        oldRead.SetException(failure);

        (await Should.ThrowAsync<TimeoutException>(() => oldInitialization)).ShouldBeSameAs(failure);
        await provider.InitializeAsync();
        provider.ResourceTypeNames.ShouldContain("Patient");
        await repository.Received(2).GetAllStructureDefinitionsAsync("4.0.1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenCanceledWaiter_WhenSharedLoadSucceeds_ThenDoesNotReplaceHealthyGeneration()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<IReadOnlyList<PackageResource>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetAllStructureDefinitionsAsync("4.0.1", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            started.TrySetResult();
            return read.Task;
        });
        var provider = CreateProvider(repository);
        using var cancellation = new CancellationTokenSource();
        var canceledWaiter = provider.InitializeAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => canceledWaiter);
        var healthyWaiter = provider.InitializeAsync();
        read.SetResult([]);
        await healthyWaiter;

        provider.ResourceTypeNames.ShouldContain("Patient");
        await repository.Received(1).GetAllStructureDefinitionsAsync("4.0.1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenCanceledSharedLoad_WhenCalledAgain_ThenRetriesUnderlyingInitialization()
    {
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetAllStructureDefinitionsAsync("4.0.1", Arg.Any<CancellationToken>()).Returns(
            Task.FromCanceled<IReadOnlyList<PackageResource>>(new CancellationToken(canceled: true)),
            Task.FromResult<IReadOnlyList<PackageResource>>([]));
        var provider = CreateProvider(repository);

        await Should.ThrowAsync<OperationCanceledException>(() => provider.InitializeAsync());

        await provider.InitializeAsync();
        provider.ResourceTypeNames.ShouldContain("Patient");
        await repository.Received(2).GetAllStructureDefinitionsAsync("4.0.1", Arg.Any<CancellationToken>());
    }

    private static IPackageResourceRepository TransientRepository(Exception failure)
    {
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetAllStructureDefinitionsAsync("4.0.1", Arg.Any<CancellationToken>()).Returns(
            Task.FromException<IReadOnlyList<PackageResource>>(failure),
            Task.FromResult<IReadOnlyList<PackageResource>>([]));
        return repository;
    }

    private static CompositeStructureDefinitionSummaryProvider CreateProvider(IPackageResourceRepository repository)
        => new(FhirVersion.R4.GetSchemaProvider(), repository,
            new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance), "4.0.1",
            NullLogger<CompositeStructureDefinitionSummaryProvider>.Instance);
}
