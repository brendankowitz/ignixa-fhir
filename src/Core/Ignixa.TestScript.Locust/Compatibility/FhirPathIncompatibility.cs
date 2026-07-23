namespace Ignixa.TestScript.Locust.Compatibility;

/// <summary>
/// A single reviewed incompatibility between Ignixa's FhirPath engine and the Python runtime's
/// <c>fhirpathpy</c>-backed adapter for a specific expression used in a specific
/// <see cref="FhirPathUsage"/>. Presence of an entry means the compiler must reject the expression
/// (LOCUST009) rather than silently emit a workload that would evaluate it differently at runtime.
/// </summary>
/// <param name="Expression">The exact FHIRPath expression string that diverges.</param>
/// <param name="Usage">The evaluation shape in which the divergence occurs.</param>
/// <param name="Reason">Reviewed, human-readable explanation of the divergence.</param>
internal sealed record FhirPathIncompatibility(string Expression, FhirPathUsage Usage, string Reason);
