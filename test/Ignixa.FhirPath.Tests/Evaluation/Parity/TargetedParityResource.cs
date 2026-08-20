using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal sealed record TargetedParityResource(
    FhirVersion Version,
    string Name,
    string Json,
    IReadOnlyList<ParityResourceFeature> Features,
    IReadOnlyList<string> SearchParameterExpressions,
    IReadOnlyList<string> ProbeExpressions,
    string? CultureName = null);
