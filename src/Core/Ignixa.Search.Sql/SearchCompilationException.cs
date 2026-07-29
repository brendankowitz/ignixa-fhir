namespace Ignixa.Search.Sql;

/// <summary>
/// Thrown by the non-<c>Try</c> entry points. The same information the <c>Try</c> entry points return as
/// a <see cref="SearchCompilationFailure"/>.
/// </summary>
public sealed class SearchCompilationException(SearchCompilationFailure failure)
    : Exception(failure?.Message, failure?.Exception)
{
    /// <summary>The failure this exception reports.</summary>
    public SearchCompilationFailure Failure { get; } = failure ?? throw new ArgumentNullException(nameof(failure));
}
