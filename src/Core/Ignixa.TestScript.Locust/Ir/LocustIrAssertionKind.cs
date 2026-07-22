namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// The category of check performed by a compiled <see cref="LocustIrAssertionCriteria"/>.
/// </summary>
public enum LocustIrAssertionKind
{
    ResponseStatus,
    ResponseCode,
    ContentType,
    ResourceType,
    Header,
    FhirPath,
    FhirPathValue,
    RequestMethod,
    RequestUrl,
}
