namespace Ignixa.TestScript.Locust.Compatibility;

/// <summary>
/// The evaluation shape a FHIRPath expression is used in when lowered to the Locust runtime.
/// The Python runtime adapter (<c>_evaluate_fhirpath</c>) exposes exactly these two shapes, so a
/// single expression can be compatible in one usage and not the other.
/// </summary>
internal enum FhirPathUsage
{
    /// <summary>Predicate usage (assertion criteria / requiresCapability): single <c>true</c> boolean, else false.</summary>
    Boolean,

    /// <summary>Single-value usage (variable extraction / fhirPathValue): FhirPath <c>toString()</c> of one primitive, else null.</summary>
    Scalar,
}
