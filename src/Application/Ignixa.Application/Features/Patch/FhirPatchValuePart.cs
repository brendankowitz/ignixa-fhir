using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// One part of an anonymous-type FHIRPath Patch value: either a value[x] (<see cref="Value"/> with its
/// <see cref="ValueType"/> suffix) or further nested <see cref="Parts"/>, never both and never neither.
/// </summary>
public sealed record FhirPatchValuePart
{
    public FhirPatchValuePart(string name, string? valueType, JsonNode? value, IReadOnlyList<FhirPatchValuePart> parts)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(parts);
        if ((value != null) == (parts.Count > 0))
        {
            throw new ArgumentException($"Value part '{name}' must have either a value[x] or nested parts, not both or neither.", nameof(parts));
        }

        Name = name;
        ValueType = valueType;
        Value = value;
        Parts = parts.ToArray();
    }

    public string Name { get; }

    public string? ValueType { get; }

    public JsonNode? Value { get; }

    public IReadOnlyList<FhirPatchValuePart> Parts { get; }
}
