namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>Wire shape of every non-2xx <c>/run</c> response body.</summary>
internal sealed record ErrorResponse(string Error);
