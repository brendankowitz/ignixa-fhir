namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Severity of a package activation issue.
/// </summary>
public enum ActivationIssueSeverity
{
    /// <summary>Nothing was activated.</summary>
    Error,

    /// <summary>The activation is durable, but follow-up work is deferred or the definitions are not yet searchable.</summary>
    Warning,

    /// <summary>The activation is durable and its follow-up work proceeds as designed.</summary>
    Information,
}
