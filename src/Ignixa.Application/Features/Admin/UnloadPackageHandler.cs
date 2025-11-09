using Ignixa.PackageManagement.Abstractions;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Admin;

/// <summary>
/// Handler for UnloadPackageCommand.
/// Unloads (deactivates) a FHIR package.
/// </summary>
public class UnloadPackageHandler : IRequestHandler<UnloadPackageCommand, UnloadPackageResult>
{
    private readonly IImplementationGuideProvider _provider;
    private readonly ILogger<UnloadPackageHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the UnloadPackageHandler class.
    /// </summary>
    /// <param name="provider">Implementation guide provider</param>
    /// <param name="logger">Logger instance</param>
    public UnloadPackageHandler(
        IImplementationGuideProvider provider,
        ILogger<UnloadPackageHandler> logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the UnloadPackageCommand.
    /// </summary>
    public async Task<UnloadPackageResult> HandleAsync(
        UnloadPackageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new InvalidOperationException("Tenant ID cannot be null or empty");
        if (string.IsNullOrWhiteSpace(request.PackageId))
            throw new InvalidOperationException("Package ID cannot be null or empty");
        if (string.IsNullOrWhiteSpace(request.Version))
            throw new InvalidOperationException("Version cannot be null or empty");

        _logger.LogInformation(
            "Unloading package {PackageId}@{Version} from tenant {TenantId}",
            request.PackageId, request.Version, request.TenantId);

        try
        {
            var count = await _provider.UnloadPackageAsync(
                request.TenantId,
                request.PackageId,
                request.Version,
                cancellationToken);

            _logger.LogInformation(
                "Package {PackageId}@{Version} unloaded. Deactivated {Count} resources",
                request.PackageId, request.Version, count);

            return new UnloadPackageResult
            {
                PackageId = request.PackageId,
                Version = request.Version,
                ResourcesDeactivated = count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to unload package {PackageId}@{Version}",
                request.PackageId, request.Version);
            throw;
        }
    }
}
