namespace Ignixa.Application.BackgroundOperations.Conformance;

public sealed record SearchParameterTransitionOrchestrationInput(
    long HideEventId,
    TimeSpan TransitionGrace);
