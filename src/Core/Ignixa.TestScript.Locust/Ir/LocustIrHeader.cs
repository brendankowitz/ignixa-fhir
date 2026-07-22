namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// A single HTTP header name/value pair carried on a compiled operation.
/// </summary>
/// <param name="Field">The header name.</param>
/// <param name="Value">The header value.</param>
public sealed record LocustIrHeader(string Field, string Value);
