namespace Ignixa.Api.Services;

/// <summary>
/// Refreshes the local consumers of conformance state after replay.
/// </summary>
public interface IConformanceCacheRefresher
{
    Task RefreshAsync(CancellationToken cancellationToken);
}
