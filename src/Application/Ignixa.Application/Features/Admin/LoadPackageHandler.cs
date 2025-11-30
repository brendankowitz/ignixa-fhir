using Ignixa.Application.Events.Package;
using Ignixa.PackageManagement;
using Ignixa.PackageManagement.Abstractions;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Admin;

/// <summary>
/// Handler for LoadPackageCommand.
/// Loads a FHIR package from the NPM registry and imports to database.
/// </summary>
public class LoadPackageHandler : IRequestHandler<LoadPackageCommand, LoadPackageResult>
{
    private readonly IImplementationGuideProvider _provider;
    private readonly IMediator _mediator;
    private readonly ILogger<LoadPackageHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the LoadPackageHandler class.
    /// </summary>
    /// <param name="provider">Implementation guide provider</param>
    /// <param name="mediator">Mediator for publishing events</param>
    /// <param name="logger">Logger instance</param>
    public LoadPackageHandler(
        IImplementationGuideProvider provider,
        IMediator mediator,
        ILogger<LoadPackageHandler> logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the LoadPackageCommand.
    /// </summary>
    public async Task<LoadPackageResult> HandleAsync(
        LoadPackageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new InvalidOperationException("Tenant ID cannot be null or empty");
        if (string.IsNullOrWhiteSpace(request.PackageId))
            throw new InvalidOperationException("Package ID cannot be null or empty");
        if (string.IsNullOrWhiteSpace(request.Version))
            throw new InvalidOperationException("Version cannot be null or empty");

        // Prevent loading core FHIR packages that conflict with pre-compiled definitions
        if (KnownPackages.IsCorePackage(request.PackageId))
        {
            _logger.LogWarning(
                "Attempted to load core FHIR package {PackageId} which conflicts with pre-compiled definitions",
                request.PackageId);

            throw new InvalidOperationException(
                $"Cannot load core FHIR package '{request.PackageId}'. " +
                $"Core FHIR definitions are already available as pre-compiled resources in Ignixa.Specification. " +
                $"Loading this package would create conflicts with SearchParameters, StructureDefinitions, and other conformance resources.");
        }

        _logger.LogInformation(
            "Loading package {PackageId}@{Version} into tenant {TenantId}",
            request.PackageId, request.Version, request.TenantId);

        try
        {
            var importResult = await _provider.LoadPackageAsync(
                request.TenantId,
                request.PackageId,
                request.Version,
                cancellationToken);

            var result = new LoadPackageResult
            {
                PackageId = importResult.PackageId,
                PackageVersion = importResult.PackageVersion,
                TotalResources = importResult.TotalResources,
                ImportedResources = importResult.ImportedResources,
                DurationMilliseconds = (long)importResult.Duration.TotalMilliseconds,
                ResourcesByType = importResult.ResourcesByType
            };

            _logger.LogInformation(
                "Package {PackageId}@{Version} loaded successfully. " +
                "Resources: {Count}, Duration: {Duration}ms",
                result.PackageId, result.PackageVersion, result.ImportedResources,
                result.DurationMilliseconds);

            // Publish PackageLoaded event for cache invalidation
            await _mediator.PublishAsync(
                new PackageLoadedEvent(
                    PackageId: result.PackageId,
                    PackageVersion: result.PackageVersion,
                    TenantId: int.Parse(request.TenantId),
                    LoadedAt: DateTimeOffset.UtcNow),
                cancellationToken);

            _logger.LogDebug("Published PackageLoaded event for {PackageId}@{Version}", result.PackageId, result.PackageVersion);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load package {PackageId}@{Version}",
                request.PackageId, request.Version);
            throw;
        }
    }
}
