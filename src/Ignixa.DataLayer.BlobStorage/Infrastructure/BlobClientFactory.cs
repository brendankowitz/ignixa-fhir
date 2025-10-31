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
    public async Task<IBlobStorageClient> CreateClientAsync()
    {
        var provider = _configuration["BlobStorage:Provider"] ?? "Local";

        return provider.Equals("local", StringComparison.OrdinalIgnoreCase)
            ? CreateLocalClient()
            : provider.Equals("azure", StringComparison.OrdinalIgnoreCase)
                ? await CreateAzureClientAsync()
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
    /// Creates an Azure Blob Storage client with container auto-initialization.
    /// </summary>
    private async Task<IBlobStorageClient> CreateAzureClientAsync()
    {
        _logger.LogInformation("Creating Azure Blob Storage client");

        var options = new AzureBlobStorageOptions();
        _configuration.GetSection("AzureBlobStorage").Bind(options);

        if (string.IsNullOrEmpty(options.ContainerName))
        {
            throw new InvalidOperationException("AzureBlobStorage:ContainerName is required when using Azure provider");
        }

        BlobServiceClient blobServiceClient;

        // Retry policy: exponential backoff for transient failures
        var clientOptions = new BlobClientOptions();
        clientOptions.Retry.Mode = RetryMode.Exponential;
        clientOptions.Retry.MaxRetries = 5;
        clientOptions.Retry.Delay = TimeSpan.FromSeconds(1);

        if (options.UseManagedIdentity)
        {
            if (string.IsNullOrEmpty(options.StorageAccountUri))
            {
                throw new InvalidOperationException("AzureBlobStorage:StorageAccountUri is required when using Managed Identity");
            }

            _logger.LogDebug("Using Managed Identity for Azure Blob Storage authentication");

            try
            {
                // Use ManagedIdentityCredential for production (secure, MI-only)
                // Use DefaultAzureCredential only for local development (flexible: MI > CLI > VS > Env)
                var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
                var credential = isDevelopment
                    ? new DefaultAzureCredential() as TokenCredential
                    : new ManagedIdentityCredential();

                blobServiceClient = new BlobServiceClient(new Uri(options.StorageAccountUri), credential, clientOptions);
                _logger.LogInformation("Successfully created Azure Blob Storage client with Managed Identity");
            }
            catch (Azure.Identity.AuthenticationFailedException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to authenticate with Azure Blob Storage using Managed Identity. " +
                    $"Ensure the application has Managed Identity enabled and proper RBAC permissions. " +
                    $"StorageAccountUri: {options.StorageAccountUri}", ex);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Invalid StorageAccountUri for Azure Blob Storage: {options.StorageAccountUri}", ex);
            }
        }
        else
        {
            if (string.IsNullOrEmpty(options.ConnectionString))
            {
                throw new InvalidOperationException("AzureBlobStorage:ConnectionString is required when UseManagedIdentity is false");
            }

            _logger.LogDebug("Using connection string for Azure Blob Storage authentication");
            blobServiceClient = new BlobServiceClient(options.ConnectionString, clientOptions);
        }

        // Ensure container exists (idempotent operation)
        var containerClient = blobServiceClient.GetBlobContainerClient(options.ContainerName);
        try
        {
            _logger.LogInformation("Ensuring container '{ContainerName}' exists", options.ContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("Container '{ContainerName}' is ready", options.ContainerName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create container '{ContainerName}'. It may already exist or you may lack permissions", options.ContainerName);
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

        // Register blob storage client as a singleton created by async factory
        services.AddSingleton<IBlobStorageClient>(sp =>
        {
            var factory = sp.GetRequiredService<BlobClientFactory>();
            // Use GetAwaiter().GetResult() to make async factory work in sync DI context
            return factory.CreateClientAsync().GetAwaiter().GetResult();
        });

        return services;
    }
}
