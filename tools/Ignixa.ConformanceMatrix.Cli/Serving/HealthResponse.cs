namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>Wire shape of <c>GET /healthz</c> — what Locust's <c>test_start</c> listener polls for.</summary>
internal sealed record HealthResponse(string Status, int Scripts, int InvalidScripts);
