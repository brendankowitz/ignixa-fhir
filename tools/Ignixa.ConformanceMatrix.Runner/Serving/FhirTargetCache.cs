using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;

namespace Ignixa.ConformanceMatrix.Runner.Serving;

/// <summary>
/// Caches one <see cref="FhirTarget"/> per distinct (base URL, FHIR version) pair for the runner
/// process's lifetime, so repeated /run calls against the same server reuse the same HttpClient
/// (connection pooling) and the same CapabilityStatement fetch instead of redoing both per request.
/// Targets are held behind <see cref="Lazy{T}"/> so a burst of first requests for the same pair —
/// exactly what a load-test ramp-up produces — constructs one HttpClient, not one per racing caller.
/// </summary>
internal sealed class FhirTargetCache(string? authHeader, Func<HttpMessageHandler>? handlerFactory = null) : IDisposable
{
    private readonly ConcurrentDictionary<(string BaseUrl, string? FhirVersion), Lazy<FhirTarget>> _targets = new();

    public FhirTarget GetOrCreate(string fhirBaseUrl, string? fhirVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fhirBaseUrl);
        var normalized = Normalize(fhirBaseUrl);
        return _targets
            .GetOrAdd((normalized, fhirVersion), key => new Lazy<FhirTarget>(() => CreateTarget(key.BaseUrl, key.FhirVersion)))
            .Value;
    }

    /// <summary>
    /// Connections are otherwise pinned to the IPs resolved at first use; a bounded pooled-connection
    /// lifetime makes a multi-hour run pick up DNS changes when the target scales or fails over
    /// instead of hammering a retired backend for the rest of the run.
    /// </summary>
    internal static SocketsHttpHandler CreatePooledHandler() => new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the HttpClient transfers to the returned FhirTarget, which this cache disposes; the catch disposes it on the only failing paths.")]
    private FhirTarget CreateTarget(string baseUrl, string? fhirVersion)
    {
        var httpClient = new HttpClient(handlerFactory?.Invoke() ?? CreatePooledHandler());
        try
        {
            httpClient.BaseAddress = new Uri(baseUrl);

            if (fhirVersion is not null)
            {
                var mediaType = $"application/fhir+json; fhirVersion={fhirVersion}";
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(mediaType));
            }

            // A misconfigured --auth-header is validated at serve startup (RunnerHost never starts with
            // a bad one); reaching an error here means it went bad after startup, which is a runner bug
            // worth a loud failure rather than silently running the target unauthenticated.
            if (authHeader is not null && AuthHeader.Apply(httpClient, authHeader) is { } error)
                throw new InvalidOperationException($"Could not apply auth header to FHIR target '{baseUrl}': {error}");

            return new FhirTarget(httpClient);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    private static string Normalize(string fhirBaseUrl) => fhirBaseUrl.TrimEnd('/') + "/";

    public void Dispose()
    {
        foreach (var target in _targets.Values)
        {
            if (target.IsValueCreated)
                target.Value.Dispose();
        }
    }
}
