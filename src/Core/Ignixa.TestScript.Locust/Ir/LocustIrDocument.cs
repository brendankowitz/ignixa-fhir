namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// The root of the versioned intermediate representation produced by the TestScript-to-Locust compiler.
/// </summary>
public sealed record LocustIrDocument
{
    public string SchemaVersion { get; init; } = LocustIrSerializer.SchemaVersion;

    public string CompilerVersion { get; init; } = "0.1.0";

    public required LocustIrMetadata Metadata { get; init; }

    public string? RequiresCapability { get; init; }

    public IReadOnlyList<LocustIrFixture> Fixtures { get; init; } = [];

    public IReadOnlyList<LocustIrVariable> Variables { get; init; } = [];

    public IReadOnlyList<LocustIrAction> Setup { get; init; } = [];

    public IReadOnlyList<LocustIrTest> Tests { get; init; } = [];

    public IReadOnlyList<LocustIrOperation> Teardown { get; init; } = [];
}
