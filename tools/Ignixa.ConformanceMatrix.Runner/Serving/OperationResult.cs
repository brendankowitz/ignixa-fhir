namespace Ignixa.ConformanceMatrix.Runner.Serving;

/// <summary>One FHIR operation's timing/outcome, surfaced for the locustfile's per-op sampler events.</summary>
internal sealed record OperationResult(
    string Name,
    string Method,
    string Path,
    int StatusCode,
    long DurationMs,
    int ResponseBytes,
    bool Passed);
