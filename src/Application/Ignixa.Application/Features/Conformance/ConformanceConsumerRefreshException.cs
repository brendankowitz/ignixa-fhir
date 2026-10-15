namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Indicates that a local conformance consumer could not be refreshed after durable state changed.
/// </summary>
public sealed class ConformanceConsumerRefreshException(string message, Exception innerException)
    : Exception(message, innerException);
