using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Ignixa.Application.Tests;

/// <summary>
/// Builds a real <see cref="PackageActivationPipeline"/> over the in-process R4 base definitions. Its I/O
/// boundaries default to substitutes: no tenants to refresh, a trigger that starts no job, a 1 s transition grace.
/// </summary>
internal static class TestPackageActivationPipeline
{
    public static readonly TimeSpan TransitionGrace = TimeSpan.FromSeconds(1);

    public static PackageActivationPipeline Create(
        IPackageResourceRepository packageRepository,
        ISourceEventStore eventStore,
        ConformanceState state,
        ISearchParameterTransitionScheduler? transitionScheduler = null,
        ITenantConfigurationStore? refreshTenants = null,
        IReindexTrigger? reindexTrigger = null,
        ConformanceLease? lease = null) =>
        new(
            packageRepository,
            eventStore,
            state,
            new PackageActivationPlanner(
                Options.Create(new SearchParameterResolutionOptions()),
                new FhirVersionContext(
                    NullLoggerFactory.Instance,
                    new SearchParameterResolutionOptions(),
                    NullFhirBaseUriProvider.Instance)),
            transitionScheduler ?? Substitute.For<ISearchParameterTransitionScheduler>(),
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = TransitionGrace }),
            TestConformanceRefresher.Create(state, tenants: refreshTenants ?? TestConformanceRefresher.Tenants()),
            lease ?? TestConformanceLease.NotHeld(),
            reindexTrigger ?? NoJobTrigger(),
            NullLogger<PackageActivationPipeline>.Instance);

    public static IReindexTrigger NoJobTrigger()
    {
        var trigger = Substitute.For<IReindexTrigger>();
        trigger.RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReindexTriggerResult(null, false, null)));
        return trigger;
    }
}
