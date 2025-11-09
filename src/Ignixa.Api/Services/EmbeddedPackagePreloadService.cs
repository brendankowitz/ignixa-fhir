using Ignixa.Application.Features.Admin;
using Ignixa.Domain.Abstractions;
using Medino;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Services;

/// <summary>
/// Hosted service that auto-loads embedded FHIR packages on startup.
/// Embedded packages (like SQL-on-FHIR ViewDefinition) are part of the application
/// and should be loaded into the system partition for all tenants to access.
/// </summary>
public class EmbeddedPackagePreloadService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmbeddedPackagePreloadService> _logger;

    public EmbeddedPackagePreloadService(
        IServiceProvider serviceProvider,
        ILogger<EmbeddedPackagePreloadService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting embedded package preload service...");

            using var scope = _serviceProvider.CreateScope();
            var configStore = scope.ServiceProvider.GetRequiredService<ITenantConfigurationStore>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Get system partition (packages are shared across all tenants)
            var systemTenant = await configStore.GetTenantConfigurationAsync(0, stoppingToken);
            if (systemTenant == null || !systemTenant.IsActive)
            {
                _logger.LogWarning("System partition (TenantId=0) not configured or inactive. Skipping embedded package preload.");
                return;
            }

            _logger.LogInformation("System partition found, preloading embedded packages...");

            // List of embedded packages to auto-load
            var embeddedPackages = new[]
            {
                ("local.ignixa.sqlonfhir", "2.1.0")
            };

            foreach (var (packageId, version) in embeddedPackages)
            {
                try
                {
                    _logger.LogInformation(
                        "Loading embedded package {PackageId}@{Version} for system partition",
                        packageId,
                        version);

                    var command = new LoadPackageCommand("0", packageId, version);
                    var result = await mediator.SendAsync(command, stoppingToken);

                    _logger.LogInformation(
                        "Successfully preloaded {PackageId}@{Version}. Imported {Count} resources",
                        packageId,
                        version,
                        result.ImportedResources);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("already loaded", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "Package {PackageId}@{Version} already loaded",
                        packageId,
                        version);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error preloading embedded package {PackageId}@{Version}. Continuing with startup.",
                        packageId,
                        version);
                    // Don't fail startup if embedded package loading fails
                }
            }

            _logger.LogInformation("Embedded package preload service completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in embedded package preload service");
            // Don't rethrow - allow server to continue even if preload fails
        }
    }
}
