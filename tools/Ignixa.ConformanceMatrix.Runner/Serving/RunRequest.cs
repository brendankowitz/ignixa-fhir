namespace Ignixa.ConformanceMatrix.Runner.Serving;

/// <summary>
/// Wire shape of <c>POST /run</c>. <see cref="TestScriptId"/> and <see cref="FhirBaseUrl"/> are
/// nullable rather than required so a missing/blank value is a 400 the endpoint reports itself,
/// not a deserialization failure with no diagnostic.
/// </summary>
internal sealed record RunRequest(
    string? TestScriptId,
    string? FhirBaseUrl,
    string Mode = "performance",
    string? FhirVersion = null,
    RunRequestOptions? Options = null);
