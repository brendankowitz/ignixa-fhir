namespace Ignixa.Application.Features.Conformance;

public sealed record SearchParameterTransitionCandidate(
    int SearchParamId,
    IReadOnlyList<long> ActivationEventIds,
    IReadOnlyList<long> DeactivationEventIds);
