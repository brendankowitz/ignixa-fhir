using System.Text.Json.Serialization;

namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// The base type for a single compiled TestScript action, discriminated in JSON by the <c>kind</c> property.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LocustIrOperation), "operation")]
[JsonDerivedType(typeof(LocustIrAssertion), "assert")]
public abstract record LocustIrAction
{
    public required string Id { get; init; }

    public string? Label { get; init; }

    public string? Description { get; init; }
}
