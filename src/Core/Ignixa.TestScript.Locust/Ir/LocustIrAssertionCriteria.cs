namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// The evaluation criteria for a compiled TestScript assertion.
/// </summary>
public sealed record LocustIrAssertionCriteria
{
    public required LocustIrAssertionKind Kind { get; init; }

    public string? Field { get; init; }

    public string? Expression { get; init; }

    public string? Value { get; init; }

    public string? Operator { get; init; }
}
