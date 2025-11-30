using Ignixa.PackageManagement.Abstractions;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Admin;

/// <summary>
/// Handler for ListPackagesQuery.
/// Lists all loaded FHIR packages.
/// </summary>
public class ListPackagesHandler : IRequestHandler<ListPackagesQuery, ListPackagesResult>
{
    private readonly IImplementationGuideProvider _provider;
    private readonly ILogger<ListPackagesHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the ListPackagesHandler class.
    /// </summary>
    /// <param name="provider">Implementation guide provider</param>
    /// <param name="logger">Logger instance</param>
    public ListPackagesHandler(
        IImplementationGuideProvider provider,
        ILogger<ListPackagesHandler> logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the ListPackagesQuery.
    /// </summary>
    public async Task<ListPackagesResult> HandleAsync(
        ListPackagesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new InvalidOperationException("Tenant ID cannot be null or empty");

        _logger.LogDebug("Listing loaded packages for tenant {TenantId}", request.TenantId);

        try
        {
            var loadedPackages = await _provider.ListLoadedPackagesAsync(request.TenantId, cancellationToken);

            var packages = loadedPackages
                .Select(p => new PackageInfo
                {
                    PackageId = p.PackageId,
                    Version = p.Version
                })
                .ToList();

            _logger.LogInformation("Found {Count} loaded packages", packages.Count);

            return new ListPackagesResult
            {
                Packages = packages.AsReadOnly()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list packages");
            throw;
        }
    }
}
