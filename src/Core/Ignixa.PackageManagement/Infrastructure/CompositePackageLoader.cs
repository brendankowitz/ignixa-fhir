using Ignixa.PackageManagement.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Composite package loader that tries multiple sources in priority order.
/// Resolution chain: Embedded (built-in) -> NPM Registry (packages.fhir.org)
/// </summary>
public partial class CompositePackageLoader : IPackageLoader
{
    private readonly IPackageLoader[] _loaders;
    private readonly ILogger<CompositePackageLoader> _logger;

    /// <summary>
    /// Creates a composite loader with the specified loaders in priority order.
    /// </summary>
    public CompositePackageLoader(
        ILogger<CompositePackageLoader> logger,
        params IPackageLoader[] loaders)
    {
        _loaders = loaders ?? throw new ArgumentNullException(nameof(loaders));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_loaders.Length == 0)
        {
            throw new ArgumentException("At least one loader must be provided", nameof(loaders));
        }
    }

    /// <summary>
    /// Downloads a package by trying each loader in sequence.
    /// First successful loader returns the package; if all fail, throws error.
    /// </summary>
    public async Task<Stream> DownloadPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();

        foreach (var loader in _loaders)
        {
            try
            {
                LogAttemptingLoad(_logger, packageId, version, loader.GetType().Name);

                var stream = await loader.DownloadPackageAsync(packageId, version, cancellationToken);

                LogSuccessfullyLoaded(_logger, packageId, version, loader.GetType().Name);

                return stream;
            }
            catch (Exception ex)
            {
                LogLoaderFailed(_logger, ex, loader.GetType().Name, packageId, version);

                errors.Add(ex);
            }
        }

        // All loaders failed
        LogAllLoadersFailed(_logger, packageId, version, _loaders.Length);

        throw new InvalidOperationException(
            $"Package '{packageId}@{version}' not found in any configured package source. " +
            $"Tried: {string.Join(", ", _loaders.Select(l => l.GetType().Name))}",
            new AggregateException("Failed to load from any package loader", errors));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Attempting to load package {PackageId}@{Version} from {LoaderType}")]
    private static partial void LogAttemptingLoad(ILogger logger, string packageId, string version, string loaderType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully loaded package {PackageId}@{Version} from {LoaderType}")]
    private static partial void LogSuccessfullyLoaded(ILogger logger, string packageId, string version, string loaderType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loader {LoaderType} could not load {PackageId}@{Version}, trying next loader")]
    private static partial void LogLoaderFailed(ILogger logger, Exception ex, string loaderType, string packageId, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load package {PackageId}@{Version} from any source. Tried {Count} loader(s)")]
    private static partial void LogAllLoadersFailed(ILogger logger, string packageId, string version, int count);
}
