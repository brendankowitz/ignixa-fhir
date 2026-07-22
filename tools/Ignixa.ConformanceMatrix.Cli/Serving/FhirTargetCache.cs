using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Ignixa.ConformanceMatrix.Cli.Commands;

namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>
/// Caches one <see cref="FhirTarget"/> per distinct (base URL, FHIR version) pair for the runner
/// process's lifetime, so repeated /run calls against the same server reuse the same HttpClient
/// (connection pooling) and the same CapabilityStatement fetch instead of redoing both per request.
/// </summary>
internal sealed class FhirTargetCache(string? authHeader, Func<HttpMessageHandler>? handlerFactory = null) : IDisposable
{
    private readonly ConcurrentDictionary<(string BaseUrl, string? FhirVersion), FhirTarget> _targets = new();

    public FhirTarget GetOrCreate(string fhirBaseUrl, string? fhirVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fhirBaseUrl);
        var normalized = Normalize(fhirBaseUrl);
        return _targets.GetOrAdd((normalized, fhirVersion), key => CreateTarget(key.BaseUrl, key.FhirVersion));
    }

    private FhirTarget CreateTarget(string baseUrl, string? fhirVersion)
    {
        var handler = handlerFactory?.Invoke();
        var httpClient = handler is not null ? new HttpClient(handler) : new HttpClient();
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
        if (authHeader is not null && RunCommand.ApplyAuthHeader(httpClient, authHeader) is { } error)
            throw new InvalidOperationException($"Could not apply auth header to FHIR target '{baseUrl}': {error}");

        return new FhirTarget(httpClient);
    }

    private static string Normalize(string fhirBaseUrl) => fhirBaseUrl.TrimEnd('/') + "/";

    public void Dispose()
    {
        foreach (var target in _targets.Values)
            target.Dispose();
    }
}
