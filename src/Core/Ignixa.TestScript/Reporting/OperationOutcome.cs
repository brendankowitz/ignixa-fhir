using Ignixa.TestScript.Client;

namespace Ignixa.TestScript.Reporting;

public sealed record OperationOutcome(
    bool Success,
    int? StatusCode = null,
    string? ErrorMessage = null,
    TimeSpan Duration = default,
    TestRequest? Request = null,
    TestResponse? Response = null);
