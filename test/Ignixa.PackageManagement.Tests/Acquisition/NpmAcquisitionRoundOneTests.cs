using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

#pragma warning disable CA2025 // Synthetic responses transfer ownership to the awaited acquirer.

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmAcquisitionRoundOneTests
{
    [Theory]
    [InlineData("https://artifacts.test/files/%2e./secret.tgz")]
    [InlineData("https://artifacts.test/files/.%2E/secret.tgz")]
    [InlineData("https://artifacts.test/files/%252e%252e/secret.tgz")]
    [InlineData("https://artifacts.test/files/a%2Fb.tgz")]
    [InlineData("https://artifacts.test/files/a%5cb.tgz")]
    [InlineData("https://artifacts.test/files/a%252fb.tgz")]
    [InlineData("https://artifacts.test/files/a%255cb.tgz")]
    [InlineData("https://artifacts.test/files/a%00b.tgz")]
    [InlineData("https://artifacts.test/files/a%3fb.tgz")]
    [InlineData("https://artifacts.test/files/a%23b.tgz")]
    [InlineData("https://artifacts.test/files/%C0%AF.tgz")]
    [InlineData("https://artifacts.test/files/%ED%A0%80.tgz")]
    [InlineData("https://artifacts.test/files/a%.tgz")]
    [InlineData("https://artifacts.test/files/a%GG.tgz")]
    [InlineData("https://artifacts.test/files//a.tgz")]
    [InlineData("https://artifacts.test/files-other/a%20b.tgz?sig=secret")]
    [InlineData("https://artifacts.test:444/files/a%20b.tgz?sig=secret")]
    [InlineData("https://other.test/files/a%20b.tgz?sig=secret")]
    [InlineData("http://artifacts.test/files/a%20b.tgz?sig=secret")]
    [InlineData("https://user@artifacts.test/files/a%20b.tgz?sig=secret")]
    [InlineData("https://artifacts.test/files/a%20b.tgz?sig=secret#fragment")]
    [InlineData("https://artifacts.test/files/a%20b.tgz?sig=%0Asecret")]
    [InlineData("https://artifacts.test/files/a%20b.tgz?sig=%")]
    public async Task GivenAmbiguousOrOutOfPrefixSignedUrl_WhenArtifactOrRedirect_ThenRejectsBeforeAuthentication(string destination)
    {
        foreach (bool redirect in new[] { false, true })
        {
            using var registry = new SyntheticNpmRegistry();
            var authCalls = new List<Uri>();
            registry.Respond = (request, _, _) =>
            {
                if (request.RequestUri!.Host == "registry.test")
                {
                    return Task.FromResult(registry.Bytes(registry.Metadata(
                        artifact: redirect ? SyntheticNpmRegistry.Artifact : destination)));
                }
                HttpResponseMessage response = registry.Bytes([], HttpStatusCode.Found);
                response.Headers.Location = new Uri(destination);
                return Task.FromResult(response);
            };
            using var acquirer = registry.Acquirer((_, uri, _) =>
            {
                authCalls.Add(uri);
                return ValueTask.FromResult<AuthenticationHeaderValue?>(null);
            });
            var error = await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
                SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(1), CancellationToken.None));
            error.Error.ShouldBe(PackageAcquisitionError.UntrustedUri);
            error.ToString().ShouldNotContain("secret");
            error.InnerException.ShouldBeNull();
            authCalls.Count.ShouldBe(redirect ? 2 : 1);
            registry.Requests.Count.ShouldBe(authCalls.Count);
            registry.Bodies.ShouldAllBe(body => body.Disposed);
        }
    }

    [Theory]
    [InlineData("next%20file.tgz?sig=%2f+%7e&x=1&x=2", "/files/next%20file.tgz?sig=%2f+%7e&x=1&x=2")]
    [InlineData("/files/%74est.tgz?sig=secret%2b", "/files/%74est.tgz?sig=secret%2b")]
    [InlineData("?sig=new%2f+%7e", "/files/test.tgz?sig=new%2f+%7e")]
    [InlineData("//ARTIFACTS.test:443/files/a%20b.tgz?sig=new%2f", "/files/a%20b.tgz?sig=new%2f")]
    public async Task GivenSignedRelativeRedirect_WhenAcquired_ThenPreservesWireSpelling(string location, string expected)
    {
        using var registry = new SyntheticNpmRegistry();
        var authCalls = new List<Uri>();
        registry.Respond = (_, count, _) =>
        {
            if (count == 2)
            {
                HttpResponseMessage response = registry.Bytes([], HttpStatusCode.Found);
                response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
                return Task.FromResult(response);
            }
            return Task.FromResult(registry.Bytes(count == 1 ? registry.Metadata() : registry.Tarball));
        };
        using var acquirer = registry.Acquirer((_, uri, _) =>
        {
            authCalls.Add(uri);
            return ValueTask.FromResult<AuthenticationHeaderValue?>(null);
        });
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(1), CancellationToken.None);
        authCalls[2].PathAndQuery.ShouldBe(expected);
        registry.Requests[2].Uri.ShouldEndWith(expected);
        registry.Bodies.ShouldAllBe(body => body.Disposed);
    }

    [Fact]
    public async Task GivenEscapedDirectoryPrefix_WhenCanonicalPathMatches_ThenAuthorizesNormalizedHostPortAndPath()
    {
        using var registry = new SyntheticNpmRegistry();
        var policy = new NpmPackageSourcePolicy("source", new Uri("https://registry.test/npm/"),
            [new Uri("https://ARTIFACTS.test:443/%66iles/")]);
        using var acquirer = registry.Acquirer();
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, CancellationToken.None);
        registry.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenEmptyRedirectReference_WhenSignedArtifactRedirectsToItself_ThenRejectsCycleWithoutRequestingDirectory()
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (_, count, _) =>
        {
            if (count == 1)
            {
                return Task.FromResult(registry.Bytes(registry.Metadata(artifact: SyntheticNpmRegistry.Artifact + "?sig=secret")));
            }
            HttpResponseMessage response = registry.Bytes([], HttpStatusCode.Found);
            response.Headers.Location = new Uri(string.Empty, UriKind.Relative);
            return Task.FromResult(response);
        };
        using var acquirer = registry.Acquirer();
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(1), CancellationToken.None));
        error.Error.ShouldBe(PackageAcquisitionError.RedirectRejected);
        registry.Requests.Count.ShouldBe(2);
        error.ToString().ShouldNotContain("secret");
    }

    [Theory]
    [InlineData("auth", PackageAcquisitionError.AuthenticationFailure)]
    [InlineData("transport", PackageAcquisitionError.TransportFailure)]
    [InlineData("digest", PackageAcquisitionError.DigestMismatch)]
    public async Task GivenSignedUrlFailure_WhenDiagnosed_ThenNeverExposesOpaqueQuery(string phase, PackageAcquisitionError expected)
    {
        using var registry = new SyntheticNpmRegistry();
        const string signed = SyntheticNpmRegistry.Artifact + "?sig=secret%2f+%7e";
        registry.Respond = (request, _, _) =>
        {
            if (request.RequestUri!.Host == "registry.test")
            {
                return Task.FromResult(registry.Bytes(registry.Metadata(artifact: signed)));
            }
            if (phase == "transport")
            {
                throw new HttpRequestException(signed);
            }
            return Task.FromResult(registry.Bytes("wrong artifact"u8.ToArray()));
        };
        using var acquirer = registry.Acquirer((_, uri, _) =>
            phase == "auth" && uri.Host != "registry.test" ? throw new IOException(signed) :
                ValueTask.FromResult<AuthenticationHeaderValue?>(null));
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(retry: new PackageAcquisitionRetryPolicy(maxAttempts: 1)), CancellationToken.None));
        error.Error.ShouldBe(expected);
        error.ToString().ShouldNotContain("secret");
        error.ToString().ShouldNotContain("sig=");
        error.InnerException.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://ARTIFACTS.test:443/files/a%20b%2Bc.tgz?sig=a%2fb%2FB+%3d&x=%7e&x=2")]
    [InlineData("https://artifacts.test/files/%74est%2Etgz?sig=%252F%2e%2e&empty=&flag")]
    [InlineData("https://artifacts.test/files/caf%C3%A9.tgz?sig=a/b?c+d==")]
    public async Task GivenEscapedSignedArtifact_WhenAcquired_ThenPreservesExactPathAndQueryBeforeAuthentication(string artifact)
    {
        using var registry = new SyntheticNpmRegistry();
        var destinations = new List<Uri>();
        registry.Respond = (request, _, _) => Task.FromResult(registry.Bytes(
            request.RequestUri!.Host == "registry.test" ? registry.Metadata(artifact: artifact) : registry.Tarball));
        using var acquirer = registry.Acquirer((_, uri, _) =>
        {
            destinations.Add(uri);
            return ValueTask.FromResult<AuthenticationHeaderValue?>(null);
        });
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        string pathAndQuery = artifact[artifact.IndexOf("/files/", StringComparison.Ordinal)..];
        destinations[1].PathAndQuery.ShouldBe(pathAndQuery);
        registry.Requests[1].Uri.ShouldEndWith(pathAndQuery);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("version")]
    [InlineData("tarball")]
    [InlineData("integrity")]
    [InlineData("root-property")]
    [InlineData("dist-property")]
    public async Task GivenMalformedUnicodeMetadata_WhenAcquired_ThenFailsPermanentlyWithSafeDiagnostic(string field)
    {
        using var registry = new SyntheticNpmRegistry();
        string metadata = Encoding.UTF8.GetString(registry.Metadata());
        metadata = field switch
        {
            "name" => metadata.Replace("test.pkg", @"secret\uD800", StringComparison.Ordinal),
            "version" => metadata.Replace("1.2.3", @"secret\uDC00", StringComparison.Ordinal),
            "tarball" => metadata.Replace(SyntheticNpmRegistry.Artifact, @"secret\uD800", StringComparison.Ordinal),
            "integrity" => Regex.Replace(metadata, "\"integrity\":\"[^\"]*\"", _ => "\"integrity\":\"secret\\uD800\""),
            "root-property" => metadata.Insert(1, "\"secret\\uD800\":0,"),
            _ => metadata.Replace("\"dist\":{", "\"dist\":{\"secret\\uD800\":0,", StringComparison.Ordinal)
        };
        registry.Respond = (request, _, _) => Task.FromResult(registry.Bytes(request.RequestUri!.Host == "registry.test"
            ? Encoding.UTF8.GetBytes(metadata) : registry.Tarball));
        using var acquirer = registry.Acquirer(timeProvider: new ManualAcquisitionTime());
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None));
        error.Error.ShouldBe(PackageAcquisitionError.InvalidMetadata);
        error.InnerException.ShouldBeNull();
        error.ToString().ShouldNotContain("secret");
        registry.Requests.Count.ShouldBe(1);
        registry.Bodies.ShouldAllBe(body => body.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenUnrelatedAuthenticatorCancellation_WhenAcquired_ThenFailsAsAuthenticationWithoutRetry(bool canceledToken)
    {
        using var registry = new SyntheticNpmRegistry();
        int calls = 0;
        using var acquirer = registry.Acquirer((_, _, _) =>
        {
            calls++;
            throw new OperationCanceledException("secret", new CancellationToken(canceledToken));
        }, new ManualAcquisitionTime());
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        task.IsCompleted.ShouldBeTrue();
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() => task);
        error.Error.ShouldBe(PackageAcquisitionError.AuthenticationFailure);
        error.ToString().ShouldNotContain("secret");
        error.InnerException.ShouldBeNull();
        calls.ShouldBe(1);
        registry.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("caller")]
    [InlineData("attempt")]
    [InlineData("total")]
    public async Task GivenBlockedAuthenticator_WhenActualTokenCancels_ThenPreservesCallerOrDeadline(string cause)
    {
        using var registry = new SyntheticNpmRegistry();
        using var caller = new CancellationTokenSource();
        var clock = new ManualAcquisitionTime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken supplied = default;
        int calls = 0;
        using var acquirer = registry.Acquirer(async (_, _, cancellationToken) =>
        {
            calls++;
            supplied = cancellationToken;
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }, clock);
        var policy = SyntheticNpmRegistry.Policy(retry: new PackageAcquisitionRetryPolicy(maxAttempts: 1));
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, caller.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cause == "caller")
        {
            await caller.CancelAsync();
            (await Should.ThrowAsync<OperationCanceledException>(() => task)).CancellationToken.ShouldBe(caller.Token);
        }
        else
        {
            clock.Advance(TimeSpan.FromSeconds(cause == "attempt" ? 121 : 301));
            (await Should.ThrowAsync<PackageAcquisitionException>(() => task)).Error.ShouldBe(PackageAcquisitionError.Timeout);
        }
        supplied.IsCancellationRequested.ShouldBeTrue();
        calls.ShouldBe(1);
        registry.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("caller")]
    [InlineData("attempt")]
    [InlineData("total")]
    public async Task GivenCacheCleanupFailureDuringCancellation_WhenAcquired_ThenPreservesPrimaryAndSanitizedSecondary(string cause)
    {
        using var directory = new OwnedCacheDirectory();
        using var registry = new SyntheticNpmRegistry();
        using var caller = new CancellationTokenSource();
        string unrelated = Path.Combine(directory.Path, "another-owner.partial");
        await File.WriteAllTextAsync(unrelated, "preserve");
        var clock = new ManualAcquisitionTime();
        string? staging = null;
        clock.BeforeTimestamp = () =>
        {
            staging = Directory.GetFiles(directory.Path, "*.partial").SingleOrDefault(path => path != unrelated);
            if (staging is null)
            {
                return;
            }
            clock.BeforeTimestamp = null;
            // Replace only this invocation's closed staging file with a directory to fault File.Delete.
            File.Delete(staging);
            Directory.CreateDirectory(staging);
            if (cause == "caller")
            {
                caller.Cancel();
                caller.Token.ThrowIfCancellationRequested();
            }
            else
            {
                clock.Advance(TimeSpan.FromSeconds(cause == "attempt" ? 121 : 301));
            }
        };
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            registry, timeProvider: clock, cache: new VerifiedPackageCache(directory.Path));
        var policy = SyntheticNpmRegistry.Policy();
        try
        {
            Exception error;
            if (cause == "caller")
            {
                // Observe the actual exception; assertion libraries may synthesize TaskCanceledException
                // from a canceled Task and discard the original exception's diagnostic data.
                var cancellation = (await CaptureFailureAsync(() =>
                    acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, caller.Token)))
                    .ShouldBeAssignableTo<OperationCanceledException>();
                cancellation.CancellationToken.ShouldBe(caller.Token);
                error = cancellation;
            }
            else
            {
                var timeout = await Should.ThrowAsync<PackageAcquisitionException>(() =>
                    acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, caller.Token));
                timeout.Error.ShouldBe(PackageAcquisitionError.Timeout);
                error = timeout;
            }
            error.Data["Ignixa.PackageManagement.CacheCleanupFailure"].ShouldBe(PackageAcquisitionError.CacheFailure);
            error.ToString().ShouldNotContain(directory.Path);
            registry.Requests.Count.ShouldBe(2);
            (await File.ReadAllTextAsync(unrelated)).ShouldBe("preserve");
            Directory.GetFiles(directory.Path, "*.tgz").ShouldBeEmpty();
            staging.ShouldNotBeNull();
        }
        finally
        {
            if (staging is not null && Directory.Exists(staging))
            {
                Directory.Delete(staging);
            }
        }

    }

    private static async Task<Exception> CaptureFailureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            return exception;
        }
        throw new InvalidOperationException("The acquisition unexpectedly succeeded.");
    }
}
