using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;

namespace Ignixa.DataLayer.BlobStorage.Infrastructure;

/// <summary>
/// Factory for creating blob storage clients based on configuration.
/// Supports both local filesystem and Azure Blob Storage implementations.
/// </summary>
public class BlobClientFactory
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BlobClientFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlobClientFactory"/> class.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    /// <param name="logger">Logger instance.</param>
    public BlobClientFactory(IConfiguration configuration, IServiceProvider serviceProvider, ILogger<BlobClientFactory> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a blob storage client based on the configured provider.
    /// </summary>
    /// <returns>An implementation of <see cref="IBlobStorageClient"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when provider is not configured or unsupported.</exception>
    public IBlobStorageClient CreateClient()
    {
        var provider = _configuration["BlobStorage:Provider"] ?? "Local";

        return provider.Equals("local", StringComparison.OrdinalIgnoreCase) ? CreateLocalClient()
            : provider.Equals("azure", StringComparison.OrdinalIgnoreCase) ? CreateAzureClient()
            : throw new InvalidOperationException($"Unknown blob storage provider: {provider}. Supported providers: 'Local', 'Azure'");
    }

    /// <summary>
    /// Creates a local filesystem blob client.
    /// </summary>
    private IBlobStorageClient CreateLocalClient()
    {
        _logger.LogInformation("Creating local file-based blob storage client");

        var options = new LocalFileBlobStorageOptions();
        _configuration.GetSection("LocalFileBlobStorage").Bind(options);

        var logger = _serviceProvider.GetRequiredService<ILogger<LocalFileBlobClient>>();
        return new LocalFileBlobClient(Microsoft.Extensions.Options.Options.Create(options), logger);
    }

    /// <summary>
    /// Creates an Azure Blob Storage client.
    /// </summary>
    private IBlobStorageClient CreateAzureClient()
    {
        _logger.LogInformation("Creating Azure Blob Storage client");

        var options = new AzureBlobStorageOptions();
        _configuration.GetSection("AzureBlobStorage").Bind(options);

        if (string.IsNullOrEmpty(options.ContainerName))
        {
            throw new InvalidOperationException("AzureBlobStorage:ContainerName is required when using Azure provider");
        }

        BlobServiceClient blobServiceClient;

        if (options.UseManagedIdentity)
        {
            if (string.IsNullOrEmpty(options.StorageAccountUri))
            {
                throw new InvalidOperationException("AzureBlobStorage:StorageAccountUri is required when using Managed Identity");
            }

            _logger.LogDebug("Using Managed Identity for Azure Blob Storage authentication");

            // Use ManagedIdentityCredential for production (secure, MI-only)
            // Use DefaultAzureCredential only for local development (flexible: MI > CLI > VS > Env)
            var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
            var credential = isDevelopment
                ? new DefaultAzureCredential() as TokenCredential
                : new ManagedIdentityCredential();

            blobServiceClient = new BlobServiceClient(new Uri(options.StorageAccountUri), credential);
        }
        else
        {
            if (string.IsNullOrEmpty(options.ConnectionString))
            {
                throw new InvalidOperationException("AzureBlobStorage:ConnectionString is required when UseManagedIdentity is false");
            }

            _logger.LogDebug("Using connection string for Azure Blob Storage authentication");
            blobServiceClient = new BlobServiceClient(options.ConnectionString);
        }

        var logger = _serviceProvider.GetRequiredService<ILogger<AzureBlobStorageClient>>();
        return new AzureBlobStorageClient(blobServiceClient, Microsoft.Extensions.Options.Options.Create(options), logger);
    }

    /// <summary>
    /// Registers blob storage services in the dependency injection container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBlobStorage(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Register configuration options
        services.Configure<LocalFileBlobStorageOptions>(options =>
        {
            configuration.GetSection("LocalFileBlobStorage").Bind(options);
        });
        services.Configure<AzureBlobStorageOptions>(options =>
        {
            configuration.GetSection("AzureBlobStorage").Bind(options);
        });

        // Register factory
        services.AddSingleton<BlobClientFactory>();

        // Register blob storage client as a singleton created by factory
        services.AddSingleton<IBlobStorageClient>(sp =>
        {
            var factory = sp.GetRequiredService<BlobClientFactory>();
            return factory.CreateClient();
        });

        return services;
    }
}
