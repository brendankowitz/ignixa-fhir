namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>Wire shape of a successful <c>POST /run</c> response. See <see cref="RunResponseMapper"/>.</summary>
internal sealed record RunResponse(
    bool Passed,
    string TestScriptId,
    long DurationMs,
    int FailedAssertionCount,
    string Summary,
    IReadOnlyList<OperationResult> Operations);
