namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// A compiled TestScript <c>test</c> element, containing the ordered actions executed for a single test case.
/// </summary>
public sealed record LocustIrTest
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? RequiresCapability { get; init; }

    public bool DiscardContextAfterExecution { get; init; }

    public IReadOnlyDictionary<string, string> InitialVariables { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<LocustIrAction> Actions { get; init; } = [];
}
