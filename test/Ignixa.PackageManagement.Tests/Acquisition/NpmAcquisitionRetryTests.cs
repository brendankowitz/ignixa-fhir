using System.Net;
using System.Net.Http.Headers;
using Ignixa.PackageManagement.Models;
using Shouldly;

#pragma warning disable CA2025 // Responses and bodies transfer to the awaited acquirer.

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmAcquisitionRetryTests
{
    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task GivenTransientStatus_WhenAcquired_ThenMakesThreeTotalAttemptsWithBoundedBackoff(int status)
    {
        using var registry = new SyntheticNpmRegistry();
        var clock = new ManualAcquisitionTime();
        registry.Respond = (_, _, _) => Task.FromResult(registry.Bytes([], (HttpStatusCode)status));
        using var acquirer = registry.Acquirer(timeProvider: clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        await AdvanceDelayAsync(task, clock, TimeSpan.FromSeconds(1), () => registry.Requests.Count > 1);
        await AdvanceDelayAsync(task, clock, TimeSpan.FromSeconds(2), () => registry.Requests.Count > 2);
        (await Should.ThrowAsync<PackageAcquisitionException>(() => task)).StatusCode.ShouldBe((HttpStatusCode)status);
        registry.Requests.Count.ShouldBe(3);
        registry.Bodies.ShouldAllBe(s => s.Disposed);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(501)]
    [InlineData(206)]
    public async Task GivenPermanentStatus_WhenAcquired_ThenNeverRetries(int status)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (_, _, _) => Task.FromResult(registry.Bytes([], (HttpStatusCode)status));
        using var acquirer = registry.Acquirer();
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None))).StatusCode.ShouldBe((HttpStatusCode)status);
        registry.Requests.Count.ShouldBe(1);
        registry.Bodies.ShouldAllBe(s => s.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenTransportFailure_WhenAcquired_ThenRetriesWithinBudgetWithoutLeakingDiagnostics(bool body)
    {
        using var registry = new SyntheticNpmRegistry();
        var clock = new ManualAcquisitionTime();
        registry.Respond = (_, _, _) => body
            ? Task.FromResult(registry.Bytes("truncated"u8.ToArray(), declaredLength: 100))
            : throw new HttpRequestException("https://user:secret@private.test/");
        using var acquirer = registry.Acquirer(timeProvider: clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        await AdvanceDelayAsync(task, clock, TimeSpan.FromSeconds(1), () => registry.Requests.Count > 1);
        await AdvanceDelayAsync(task, clock, TimeSpan.FromSeconds(2), () => registry.Requests.Count > 2);
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() => task);
        error.Error.ShouldBe(PackageAcquisitionError.TransportFailure);
        error.ToString().ShouldNotContain("secret");
        registry.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GivenRepeatedTransientFailures_WhenAcquired_ThenCapsExponentialFullJitterAtMaximum()
    {
        using var registry = new SyntheticNpmRegistry();
        var clock = new ManualAcquisitionTime();
        registry.Respond = (_, _, _) => Task.FromResult(registry.Bytes([], HttpStatusCode.ServiceUnavailable));
        var retry = new PackageAcquisitionRetryPolicy(maxAttempts: 8);
        using var acquirer = registry.Acquirer(timeProvider: clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(retry: retry), CancellationToken.None);
        int attempt = 1;
        foreach (int ceiling in new[] { 1, 2, 4, 8, 16, 30, 30 })
        {
            await AdvanceDelayAsync(task, clock, TimeSpan.FromSeconds(ceiling), () => registry.Requests.Count > attempt);
            attempt++;
        }
        _ = await Should.ThrowAsync<PackageAcquisitionException>(() => task);
        registry.Requests.Count.ShouldBe(8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenRetryAfter_WhenItFits_ThenDoesNotRetryEarly(bool date)
    {
        using var registry = new SyntheticNpmRegistry();
        var clock = new ManualAcquisitionTime();
        registry.Respond = (request, count, _) =>
        {
            if (count == 1)
            {
                HttpResponseMessage response = registry.Bytes([], HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = date
                    ? new RetryConditionHeaderValue(clock.GetUtcNow() + TimeSpan.FromSeconds(4))
                    : new RetryConditionHeaderValue(TimeSpan.FromSeconds(4));
                return Task.FromResult(response);
            }
            return Task.FromResult(registry.Bytes(request.RequestUri!.Host == "registry.test" ? registry.Metadata() : registry.Tarball));
        };
        using var acquirer = registry.Acquirer(timeProvider: clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        (await clock.WaitForDelayAsync(TimeSpan.FromSeconds(4))).ShouldBe(TimeSpan.FromSeconds(4));
        clock.Advance(TimeSpan.FromSeconds(3));
        registry.Requests.Count.ShouldBe(1);
        task.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        _ = await task.WaitAsync(TimeSpan.FromSeconds(5));
        registry.Requests.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(31, 300)]
    [InlineData(6, 5)]
    [InlineData(5, 5)]
    public async Task GivenRetryAfterBeyondMaxOrRemainingDeadline_WhenAcquired_ThenStopsRatherThanRetryingEarly(int delay, int total)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (_, _, _) =>
        {
            HttpResponseMessage response = registry.Bytes([], HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(delay));
            return Task.FromResult(response);
        };
        var retry = new PackageAcquisitionRetryPolicy(attemptTimeout: TimeSpan.FromSeconds(total),
            totalTimeout: TimeSpan.FromSeconds(total), maxDelay: TimeSpan.FromSeconds(Math.Min(30, total)));
        using var acquirer = registry.Acquirer(timeProvider: new ManualAcquisitionTime());
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(retry: retry), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.HttpFailure);
        registry.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenCancellationDuringBackoff_WhenCancelled_ThenStopsAsCallerCancellation()
    {
        using var registry = new SyntheticNpmRegistry();
        using var caller = new CancellationTokenSource();
        var clock = new ManualAcquisitionTime();
        registry.Respond = (_, _, _) =>
        {
            HttpResponseMessage response = registry.Bytes([], HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return Task.FromResult(response);
        };
        using var acquirer = registry.Acquirer(timeProvider: clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), caller.Token);
        _ = await clock.WaitForDelayAsync(TimeSpan.FromSeconds(1));
        await caller.CancelAsync();
        (await Should.ThrowAsync<OperationCanceledException>(() => task)).CancellationToken.ShouldBe(caller.Token);
        registry.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenBlockedBody_WhenCallerCancels_ThenDisposesAndNeverRetries(bool artifact)
    {
        using var registry = new SyntheticNpmRegistry();
        using var body = new PendingAcquisitionStream();
        using var caller = new CancellationTokenSource();
        registry.Respond = (_, count, _) => Task.FromResult(artifact && count == 1
            ? registry.Bytes(registry.Metadata())
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
        using var acquirer = registry.Acquirer();
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), caller.Token);
        await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await caller.CancelAsync();
        (await Should.ThrowAsync<OperationCanceledException>(() => task)).CancellationToken.ShouldBe(caller.Token);
        body.Disposed.ShouldBeTrue();
        registry.Requests.Count.ShouldBe(artifact ? 2 : 1);
    }

    [Fact]
    public async Task GivenBlockedBody_WhenAttemptExpires_ThenRetriesAndSharesOverallDeadline()
    {
        using var registry = new SyntheticNpmRegistry();
        var clock = new ManualAcquisitionTime();
        var bodies = new List<PendingAcquisitionStream>();
        registry.Respond = (_, _, _) =>
        {
            var body = new PendingAcquisitionStream();
            bodies.Add(body);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
        };
        var retry = new PackageAcquisitionRetryPolicy(attemptTimeout: TimeSpan.FromSeconds(2),
            totalTimeout: TimeSpan.FromSeconds(3), maxDelay: TimeSpan.FromSeconds(1));
        using var acquirer = registry.Acquirer(timeProvider: clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(retry: retry), CancellationToken.None);
        await bodies[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(2));
        await AdvanceDelayAsync(task, clock, TimeSpan.FromSeconds(1), () => registry.Requests.Count > 1);
        while (bodies.Count < 2)
        {
            if (task.IsCompleted)
            {
                await task;
            }
            await Task.Yield();
        }
        await bodies[1].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(1));
        (await Should.ThrowAsync<PackageAcquisitionException>(() => task.WaitAsync(TimeSpan.FromSeconds(5))))
            .Error.ShouldBe(PackageAcquisitionError.Timeout);
        registry.Requests.Count.ShouldBe(2);
        bodies.ShouldAllBe(b => b.Disposed);
    }

    [Fact]
    public async Task GivenAuthenticationFailure_WhenAcquired_ThenIsPermanentAndSanitized()
    {
        using var registry = new SyntheticNpmRegistry();
        using var acquirer = registry.Acquirer((_, _, _) => throw new IOException("secret credential unavailable"));
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None));
        error.Error.ShouldBe(PackageAcquisitionError.AuthenticationFailure);
        error.ToString().ShouldNotContain("secret");
        registry.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(404)]
    public async Task GivenTransportExceptionWithPermanentHttpStatus_WhenAcquired_ThenDoesNotRetry(int status)
    {
        using var registry = new SyntheticNpmRegistry();
        var clock = new ManualAcquisitionTime();
        registry.Respond = (_, _, _) => throw new HttpRequestException("secret", null, (HttpStatusCode)status);
        using var acquirer = registry.Acquirer(timeProvider: clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        // Any scheduled backoff instead of immediate permanent failure violates the contract.
        task.IsCompleted.ShouldBeTrue();
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() => task);
        error.Error.ShouldBe(PackageAcquisitionError.HttpFailure);
        error.StatusCode.ShouldBe((HttpStatusCode)status);
        registry.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenMalformedRetryAfter_WhenAcquired_ThenStopsInsteadOfIgnoringIt()
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (_, _, _) =>
        {
            HttpResponseMessage response = registry.Bytes([], HttpStatusCode.ServiceUnavailable);
            response.Headers.TryAddWithoutValidation("Retry-After", "not-a-time");
            return Task.FromResult(response);
        };
        using var acquirer = registry.Acquirer(timeProvider: new ManualAcquisitionTime());
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        task.IsCompleted.ShouldBeTrue();
        (await Should.ThrowAsync<PackageAcquisitionException>(() => task)).Error.ShouldBe(PackageAcquisitionError.HttpFailure);
        registry.Requests.Count.ShouldBe(1);
    }

    private static async Task AdvanceDelayAsync(Task task, ManualAcquisitionTime clock, TimeSpan ceiling, Func<bool> hasProgressed)
    {
        TimeSpan delay = await clock.WaitForDelayAsync(ceiling, task, hasProgressed);
        delay.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        delay.ShouldBeLessThanOrEqualTo(ceiling);
        clock.Advance(delay);
    }
}
