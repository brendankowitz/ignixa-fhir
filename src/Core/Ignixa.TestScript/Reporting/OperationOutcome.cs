namespace Ignixa.TestScript.Reporting;

public sealed record OperationOutcome(
    bool Success,
    int? StatusCode = null,
    string? ErrorMessage = null,
    TimeSpan Duration = default);
