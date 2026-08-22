using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal sealed record GeneratedParityResource(
    FhirVersion Version,
    string ResourceType,
    string Json,
    IReadOnlyList<string> Expressions);
