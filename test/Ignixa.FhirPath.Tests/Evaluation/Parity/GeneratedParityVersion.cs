using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal sealed record GeneratedParityVersion(
    FhirVersion Version,
    IReadOnlyList<GeneratedParityResource> Resources);
