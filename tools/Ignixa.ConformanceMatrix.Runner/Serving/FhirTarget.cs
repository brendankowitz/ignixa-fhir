using System.Diagnostics.CodeAnalysis;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.ConformanceMatrix.Runner.Serving;

/// <summary>
/// One cached FHIR target: an <see cref="HttpClient"/> configured for a specific (base URL, FHIR
/// version) pair, and its CapabilityStatement fetched once on success and reused for every /run call
/// against this target. Two deliberate choices here:
/// <list type="bullet">
/// <item>The cache holds the raw CapabilityStatement JSON, and <see cref="GetCapabilityStatementAsync"/>
/// parses a fresh <see cref="ResourceJsonNode"/> per call. <see cref="ResourceJsonNode.ToElement"/>
/// and the source-node tree underneath it memoize lazily with no synchronization, so a single node
/// instance shared across concurrent runs can observe partially published caches; per-run instances
/// keep that memoization private to one evaluation, matching how TestScriptContext deep-clones
/// fixture and response bodies.</item>
/// <item>A failed fetch is not cached. If the runner starts before the FHIR server finishes warming
/// and the first /metadata call fails, the next /run retries instead of disabling
/// requiresCapability gating for the process lifetime. Concurrent first callers still single-flight
/// through one fetch via the semaphore.</item>
/// </list>
/// </summary>
internal sealed class FhirTarget : IDisposable
{
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private volatile string? _capabilityStatementJson;

    public FhirTarget(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }

    /// <summary>
    /// Returns a freshly parsed CapabilityStatement for this run, or <see langword="null"/> when
    /// /metadata is (still) unavailable — capability gating fails open for that run only.
    /// </summary>
    public async Task<ResourceJsonNode?> GetCapabilityStatementAsync(CancellationToken cancellationToken)
    {
        var json = _capabilityStatementJson ?? await FetchAndCacheAsync(cancellationToken);
        return json is null ? null : JsonSourceNodeFactory.Parse(json);
    }

    private async Task<string?> FetchAndCacheAsync(CancellationToken cancellationToken)
    {
        await _fetchLock.WaitAsync(cancellationToken);
        try
        {
            if (_capabilityStatementJson is { } cached)
                return cached;

            var body = await FetchCapabilityStatementJsonAsync(cancellationToken);
            if (body is not null)
                _capabilityStatementJson = body;
            return body;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Capability gating deliberately fails open on any fetch/parse failure; the failure is reported and retried on the next run rather than propagated.")]
    private async Task<string?> FetchCapabilityStatementJsonAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(new Uri("metadata", UriKind.Relative), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"warning: could not fetch /metadata (HTTP {(int)response.StatusCode}); requiresCapability gating fails open for this run and the fetch is retried on the next");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _ = JsonSourceNodeFactory.Parse(body);
            return body;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: could not fetch /metadata ({ex.GetType().Name}: {ex.Message}); requiresCapability gating fails open for this run and the fetch is retried on the next");
            return null;
        }
    }

    public void Dispose()
    {
        HttpClient.Dispose();
        _fetchLock.Dispose();
    }
}
