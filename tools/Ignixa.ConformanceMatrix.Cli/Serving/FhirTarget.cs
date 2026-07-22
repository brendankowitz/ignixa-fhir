using Ignixa.ConformanceMatrix.Cli.Commands;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>
/// One cached FHIR target: an <see cref="HttpClient"/> configured for a specific (base URL, FHIR
/// version) pair, and its CapabilityStatement fetched at most once and reused for every /run call
/// against this target. <see cref="Lazy{T}"/>'s default thread-safety mode single-flights the first
/// concurrent callers into one /metadata request rather than one per in-flight request.
/// </summary>
internal sealed class FhirTarget : IDisposable
{
    private readonly Lazy<Task<ResourceJsonNode?>> _capabilityStatement;

    public FhirTarget(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        HttpClient = httpClient;
        _capabilityStatement = new Lazy<Task<ResourceJsonNode?>>(
            () => RunCommand.FetchCapabilityStatementAsync(HttpClient, CancellationToken.None));
    }

    public HttpClient HttpClient { get; }

    public Task<ResourceJsonNode?> GetCapabilityStatementAsync() => _capabilityStatement.Value;

    public void Dispose() => HttpClient.Dispose();
}
