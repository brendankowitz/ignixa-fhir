namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// A compiled TestScript assertion action that validates a prior operation's request or response.
/// </summary>
public sealed record LocustIrAssertion : LocustIrAction
{
    public required LocustIrAssertionCriteria Criteria { get; init; }

    public bool WarningOnly { get; init; }

    public string Direction { get; init; } = "response";

    public string? SourceId { get; init; }

    public string? AnyOfGroupId { get; init; }

    public string? WhenResponseSourceId { get; init; }

    public IReadOnlyList<int> WhenResponseStatuses { get; init; } = [];
}
