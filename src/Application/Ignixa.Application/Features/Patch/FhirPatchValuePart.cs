using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// One part of an anonymous-type FHIRPath Patch value: either a value[x] (<see cref="Value"/> with its
/// <see cref="ValueType"/> suffix) or further nested <see cref="Parts"/>.
/// </summary>
public sealed record FhirPatchValuePart(
    string Name,
    string? ValueType,
    JsonNode? Value,
    IReadOnlyList<FhirPatchValuePart> Parts);
