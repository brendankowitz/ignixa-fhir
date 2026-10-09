namespace Ignixa.Application.Features.Conformance;

public sealed class ReindexTriggerUnavailableException(string message, Exception innerException)
    : Exception(message, innerException)
;
