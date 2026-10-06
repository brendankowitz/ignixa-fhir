using System.Net;
using System.Net.Http.Headers;
using Ignixa.PackageManagement.Models;
using Shouldly;

#pragma warning disable CA2025 // Responses transfer to the awaited acquirer.

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class ManualAcquisitionTimeTests
{
    [Fact]
    public async Task GivenSubMillisecondJitter_WhenRetriesCompleteSynchronously_ThenDriverDoesNotRequireATimer()
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (_, _, _) => Task.FromResult(registry.Bytes([], HttpStatusCode.ServiceUnavailable));
        var clock = new ManualAcquisitionTime();
        using var acquirer = registry.Acquirer(timeProvider: clock);
        var retry = new PackageAcquisitionRetryPolicy(initialDelay: TimeSpan.FromTicks(1), maxDelay: TimeSpan.FromTicks(1));
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(retry: retry), CancellationToken.None);
        task.IsCompleted.ShouldBeTrue();
        registry.Requests.Count.ShouldBe(3);
        (await clock.WaitForDelayAsync(TimeSpan.FromTicks(1), task)).ShouldBe(TimeSpan.Zero);
        (await Should.ThrowAsync<PackageAcquisitionException>(() => task)).Error.ShouldBe(PackageAcquisitionError.HttpFailure);
    }

    [Fact]
    public async Task GivenZeroFirstDelay_WhenNextRetryIsAlreadyWaiting_ThenDriverDoesNotAdvanceTheLaterRetryEarly()
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (request, count, _) =>
        {
            HttpResponseMessage response = registry.Bytes(count > 2
                ? request.RequestUri!.Host == "registry.test" ? registry.Metadata() : registry.Tarball : [],
                count > 2 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests);
            if (count <= 2)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(count == 1 ? 0 : 2));
            }
            return Task.FromResult(response);
        };
        var clock = new ManualAcquisitionTime();
        using var acquirer = registry.Acquirer(timeProvider: clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        registry.Requests.Count.ShouldBe(2);
        (await clock.WaitForDelayAsync(TimeSpan.FromSeconds(1), task, () => registry.Requests.Count > 1)).ShouldBe(TimeSpan.Zero);
        (await clock.WaitForDelayAsync(TimeSpan.FromSeconds(2), task)).ShouldBe(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(1));
        registry.Requests.Count.ShouldBe(2);
        task.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        _ = await task.WaitAsync(TimeSpan.FromSeconds(5));
        registry.Requests.Count.ShouldBe(4);
    }
}
