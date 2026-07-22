using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// A compiled TestScript fixture, including any resource variants to be created at runtime.
/// </summary>
/// <param name="Id">The stable fixture identifier.</param>
/// <param name="Autocreate">Whether the runtime should automatically create the fixture resource.</param>
/// <param name="Autodelete">Whether the runtime should automatically delete the fixture resource.</param>
/// <param name="Variants">The raw resource payload variants associated with the fixture.</param>
public sealed record LocustIrFixture(
    string Id,
    bool Autocreate,
    bool Autodelete,
    IReadOnlyList<JsonObject> Variants);
