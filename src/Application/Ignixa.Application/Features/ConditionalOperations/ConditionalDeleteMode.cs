namespace Ignixa.Application.Features.ConditionalOperations;

/// <summary>
/// Conditional delete mode configuration.
/// </summary>
public enum ConditionalDeleteMode
{
    /// <summary>
    /// Only allow single match deletes (error on multiple matches).
    /// </summary>
    SingleMatch,

    /// <summary>
    /// Allow multiple match deletes (requires _count parameter).
    /// </summary>
    MultipleMatches,

    /// <summary>
    /// Support both single and multiple modes (default).
    /// </summary>
    BothModes
}
