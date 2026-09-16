using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>Opt-in exact-version acquisition from an explicitly trusted standard NPM registry.</summary>
/// <remarks>
/// Owns its HttpClient and privately configured transport. Production callers cannot replace or
/// mutate that transport. The optional certificate callback controls TLS server trust only.
/// The extractor, authenticator and TimeProvider remain caller-owned. Authentication returns
/// credentials for each approved source/destination independently; it must not blindly reuse them
/// across origins. Only Authorization is supported, never ambient cookies.
/// </remarks>
public sealed class NpmPackageAcquirer : IDisposable
{
    private readonly PackageExtractor _extractor;
    private readonly HttpClient _client;
    private readonly HttpMessageHandler? _ownedHandler;
    private readonly TimeProvider _timeProvider;
    private readonly VerifiedPackageCache? _cache;
    private readonly Func<string, Uri, CancellationToken, ValueTask<AuthenticationHeaderValue?>>? _authenticate;

    public NpmPackageAcquirer(
        PackageExtractor extractor,
        Func<string, Uri, CancellationToken, ValueTask<AuthenticationHeaderValue?>>? authenticate = null,
        TimeProvider? timeProvider = null, VerifiedPackageCache? cache = null,
        RemoteCertificateValidationCallback? validateServerCertificate = null)
        : this(extractor, CreateTransport(extractor, validateServerCertificate), authenticate, timeProvider, cache, ownsHandler: true)
    {
    }

    // Arbitrary handlers are a friend-test seam, never a selectable production transport.
    internal NpmPackageAcquirer(
        PackageExtractor extractor, HttpMessageHandler trustedHandler,
        Func<string, Uri, CancellationToken, ValueTask<AuthenticationHeaderValue?>>? authenticate = null,
        TimeProvider? timeProvider = null, VerifiedPackageCache? cache = null)
        : this(extractor, trustedHandler, authenticate, timeProvider, cache, ownsHandler: false)
    {
    }

    private NpmPackageAcquirer(
        PackageExtractor extractor, HttpMessageHandler handler,
        Func<string, Uri, CancellationToken, ValueTask<AuthenticationHeaderValue?>>? authenticate,
        TimeProvider? timeProvider, VerifiedPackageCache? cache, bool ownsHandler)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(handler);
        _extractor = extractor;
        _authenticate = authenticate;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cache = cache;
        _ownedHandler = ownsHandler ? handler : null;
        _client = new HttpClient(handler, disposeHandler: false);
        _client.Timeout = Timeout.InfiniteTimeSpan;
    }

    private static SocketsHttpHandler CreateTransport(
        PackageExtractor extractor, RemoteCertificateValidationCallback? validateServerCertificate)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseProxy = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = validateServerCertificate
            }
        };
    }

    public async Task<AcquiredNpmPackage> AcquireAsync(
        NpmPackageIdentity identity, NpmPackageSourcePolicy policy, CancellationToken cancellationToken,
        NpmPackageIntegrity? integrityPin = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();
        long totalStart = _timeProvider.GetTimestamp();
        using var totalTimeout = new CancellationTokenSource(policy.Retry.TotalTimeout, _timeProvider);
        using var totalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, totalTimeout.Token);
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                RetryablePackageException failure;
                try
                {
                    return await RunAttemptAsync(identity, policy, integrityPin, totalStart,
                        totalCancellation.Token, cancellationToken).ConfigureAwait(false);
                }
                catch (RetryablePackageException exception)
                {
                    failure = exception;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    var canceled = new OperationCanceledException(cancellationToken);
                    PackageAcquisitionException.PreserveCleanupFailure(failure.Diagnostic, canceled);
                    throw canceled;
                }
                TimeSpan remaining = policy.Retry.TotalTimeout - _timeProvider.GetElapsedTime(totalStart);
                if (totalTimeout.IsCancellationRequested || remaining <= TimeSpan.Zero)
                {
                    var timeout = new PackageAcquisitionException(PackageAcquisitionError.Timeout);
                    PackageAcquisitionException.PreserveCleanupFailure(failure.Diagnostic, timeout);
                    throw timeout;
                }
                if (attempt >= policy.Retry.MaxAttempts ||
                    failure.Diagnostic.Data[PackageAcquisitionException.CacheCleanupFailureDataKey] is PackageAcquisitionError.CacheFailure)
                {
                    throw failure.Diagnostic;
                }

                double ceiling = Math.Min(policy.Retry.MaxDelay.TotalMilliseconds,
                    policy.Retry.InitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                double jitter = RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;
                TimeSpan delay = failure.RetryAfter ?? TimeSpan.FromMilliseconds(jitter * ceiling);
                if (delay > policy.Retry.MaxDelay || delay >= remaining)
                {
                    throw failure.Diagnostic;
                }

                // Attempt timers are not the backoff budget. Only the shared deadline/caller governs delay.
                await Task.Delay(delay, _timeProvider, totalCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested ||
            totalCancellation.IsCancellationRequested || _timeProvider.GetElapsedTime(totalStart) >= policy.Retry.TotalTimeout)
        {
            Exception failure = cancellationToken.IsCancellationRequested
                ? new OperationCanceledException(cancellationToken)
                : new PackageAcquisitionException(PackageAcquisitionError.Timeout);
            PackageAcquisitionException.PreserveCleanupFailure(exception, failure);
            throw failure;
        }
    }

    private async Task<AcquiredNpmPackage> RunAttemptAsync(
        NpmPackageIdentity identity, NpmPackageSourcePolicy policy, NpmPackageIntegrity? integrityPin,
        long totalStart, CancellationToken totalCancellation, CancellationToken cancellationToken)
    {
        long attemptStart = _timeProvider.GetTimestamp();
        using var attemptTimeout = new CancellationTokenSource(policy.Retry.AttemptTimeout, _timeProvider);
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(totalCancellation, attemptTimeout.Token);
        void CheckAttempt()
        {
            cancellationToken.ThrowIfCancellationRequested();
            attemptCancellation.Token.ThrowIfCancellationRequested();
            if (_timeProvider.GetElapsedTime(totalStart) >= policy.Retry.TotalTimeout ||
                _timeProvider.GetElapsedTime(attemptStart) >= policy.Retry.AttemptTimeout)
            {
                throw new RetryablePackageException(PackageAcquisitionError.Timeout);
            }
        }

        try
        {
            CheckAttempt();
            AcquiredNpmPackage result = await AcquireAttemptAsync(
                identity, policy, integrityPin, CheckAttempt, attemptCancellation.Token).ConfigureAwait(false);
            CheckAttempt();
            return result;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested &&
            (attemptCancellation.IsCancellationRequested ||
             _timeProvider.GetElapsedTime(attemptStart) >= policy.Retry.AttemptTimeout ||
             _timeProvider.GetElapsedTime(totalStart) >= policy.Retry.TotalTimeout))
        {
            var timeout = new RetryablePackageException(PackageAcquisitionError.Timeout);
            PackageAcquisitionException.PreserveCleanupFailure(exception, timeout.Diagnostic);
            throw timeout;
        }
    }

    private async Task<AcquiredNpmPackage> AcquireAttemptAsync(
        NpmPackageIdentity identity, NpmPackageSourcePolicy policy, NpmPackageIntegrity? integrityPin,
        Action checkBudget, CancellationToken cancellationToken)
    {
        // Only this locally constructed exact identifier may contain escaped scope/version separators.
        var metadataUri = new Uri(policy.RegistryBaseUri,
            $"{Uri.EscapeDataString(identity.Name)}/{Uri.EscapeDataString(identity.Version)}");
        byte[] metadata = await GetBytesAsync(metadataUri, policy, metadata: true, cancellationToken).ConfigureAwait(false);
        checkBudget();
        NpmSelectedVersion selected = NpmSelectedVersion.Parse(metadata, identity, policy, integrityPin);
        checkBudget();
        byte[]? cached = _cache is null ? null :
            await _cache.ReadAsync(policy, identity, selected.Integrity, cancellationToken).ConfigureAwait(false);
        checkBudget();
        byte[] tarball = cached ?? await GetBytesAsync(selected.Tarball, policy, metadata: false, cancellationToken).ConfigureAwait(false);
        checkBudget();
        VerifyDigest(tarball, selected.Integrity, cancellationToken);
        checkBudget();
        using var stream = new MemoryStream(tarball, writable: false);
        StrictPackageExtractionResult extraction = await _extractor.ExtractStrictAsync(
            stream, policy.ExtractionLimits, cancellationToken).ConfigureAwait(false);
        checkBudget();
        if (extraction.Manifest.Name != identity.Name || extraction.Manifest.Version != identity.Version)
        {
            throw new PackageAcquisitionException(PackageAcquisitionError.ManifestIdentityMismatch);
        }

        if (_cache is not null && cached is null)
        {
            await _cache.PublishAsync(policy, identity, selected.Integrity, tarball, checkBudget, cancellationToken).ConfigureAwait(false);
        }

        checkBudget();
        cancellationToken.ThrowIfCancellationRequested();
        return new AcquiredNpmPackage(policy.SourceId, identity, selected.Integrity, extraction);
    }

    private async Task<byte[]> GetBytesAsync(
        Uri uri, NpmPackageSourcePolicy policy, bool metadata, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { uri.AbsoluteUri };
        for (int redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(metadata ? "application/json" : "application/octet-stream"));
            if (_authenticate is not null)
            {
                try
                {
                    request.Headers.Authorization = await _authenticate(policy.SourceId, uri, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Auth infrastructure is a trust boundary; its exception may contain credentials.
                    throw new PackageAcquisitionException(PackageAcquisitionError.AuthenticationFailure);
                }
            }

            try
            {
                using HttpResponseMessage response = await _client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode))
                {
                    if (redirects >= policy.MaxRedirects || response.Headers.Location is not { } location)
                    {
                        throw new PackageAcquisitionException(PackageAcquisitionError.RedirectRejected);
                    }

                    Uri destination = ResolveRedirect(uri, location);
                    if (!IsAuthorized(destination, policy, metadata))
                    {
                        throw new PackageAcquisitionException(PackageAcquisitionError.UntrustedUri);
                    }

                    if (!visited.Add(destination.AbsoluteUri))
                    {
                        throw new PackageAcquisitionException(PackageAcquisitionError.RedirectRejected);
                    }

                    uri = destination;
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    if (IsRetryableStatus(response.StatusCode))
                    {
                        throw new RetryablePackageException(PackageAcquisitionError.HttpFailure, response.StatusCode,
                            GetRetryAfter(response));
                    }
                    throw new PackageAcquisitionException(PackageAcquisitionError.HttpFailure, response.StatusCode);
                }

                if (response.Content.Headers.ContentEncoding.Any(encoding => !string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new PackageAcquisitionException(PackageAcquisitionError.UnsupportedContentEncoding);
                }

                int limit = metadata ? policy.MaxMetadataBytes : policy.ExtractionLimits.MaxCompressedBytes;
                PackageAcquisitionError limitError = metadata ? PackageAcquisitionError.MetadataSizeLimit : PackageAcquisitionError.CompressedSizeLimit;
                if (response.Content.Headers.ContentLength > limit)
                {
                    throw new PackageAcquisitionException(limitError);
                }

                await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                byte[] bytes = await BoundedPackageRead.ReadAsync(body, limit, limitError, cancellationToken).ConfigureAwait(false);
                if (bytes.LongLength < response.Content.Headers.ContentLength)
                {
                    throw new HttpRequestException("Truncated response.");
                }
                return bytes;
            }
            catch (HttpRequestException exception) when (HasTlsAuthenticationFailure(exception))
            {
                throw new PackageAcquisitionException(PackageAcquisitionError.TransportFailure);
            }
            catch (HttpRequestException exception) when (exception.StatusCode is { } status)
            {
                if (!IsRetryableStatus(status))
                {
                    throw new PackageAcquisitionException(PackageAcquisitionError.HttpFailure, status);
                }
                throw new RetryablePackageException(PackageAcquisitionError.HttpFailure, status);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PackageAcquisitionException(PackageAcquisitionError.TransportFailure);
            }
            catch (Exception exception) when (exception is HttpRequestException ||
                exception is IOException and not PackageExtractionException)
            {
                // This catch is deliberately confined to transport and body reads, NEVER extraction/cache.
                throw new RetryablePackageException(PackageAcquisitionError.TransportFailure);
            }
        }
    }

    private static Uri ResolveRedirect(Uri origin, Uri location)
    {
        if (!NpmSourceUri.TryResolveRedirect(origin, location, out Uri? resolved))
        {
            throw new PackageAcquisitionException(PackageAcquisitionError.UntrustedUri);
        }

        return NpmSourceUri.PreserveWireUri(resolved!);
    }

    private static bool HasTlsAuthenticationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }
        }
        return false;
    }

    internal static bool IsAuthorized(Uri uri, NpmPackageSourcePolicy policy, bool metadata) =>
        NpmSourceUri.IsUnambiguousHttps(uri) &&
        (metadata ? NpmSourceUri.IsWithin(uri, policy.RegistryBaseUri) :
            policy.AllowedArtifactPrefixes.Any(prefix => NpmSourceUri.IsWithin(uri, prefix)));

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool IsRetryableStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? value = response.Headers.RetryAfter;
        if (value is null && response.Headers.Contains("Retry-After"))
        {
            throw new PackageAcquisitionException(PackageAcquisitionError.HttpFailure, response.StatusCode);
        }
        TimeSpan? delay = value?.Delta ?? (value?.Date - _timeProvider.GetUtcNow());
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static void VerifyDigest(byte[] bytes, NpmPackageIntegrity integrity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] actual = SHA512.HashData(bytes);
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(integrity.Integrity[7..])))
        {
            throw new PackageAcquisitionException(PackageAcquisitionError.DigestMismatch);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        _client.Dispose();
        _ownedHandler?.Dispose();
    }
}
