// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Harness;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Search;
using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ignixa.Api.E2ETests._Infrastructure;

/// <summary>
/// Test fixture for E2E tests using WebApplicationFactory.
/// Configures the Ignixa API with in-memory storage for testing.
/// Program is public to support WebApplicationFactory in tests.
/// </summary>
public class IgnixaApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _testDataPath;
    private readonly string _sqlConnectionString;

    private static bool UseSqlServer =>
        Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) != true;

    private static string GetSqlConnectionString()
    {
        var connStr = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(connStr))
            return connStr;

        // Default for local docker-compose
        var password = Environment.GetEnvironmentVariable("SQL_SA_PASSWORD");

        if (!string.IsNullOrEmpty(password))
        {
            var database = $"FhirTest_{Guid.NewGuid():N}"; // Unique DB per test run
            return $"Server=localhost,1433;Database={database};User Id=sa;Password={password};TrustServerCertificate=true;Encrypt=false";
        }

        // default local test instance
        return "server=(local);Initial Catalog=FHIR_R4;Integrated Security=true;TrustServerCertificate=true";
    }

    public IgnixaApiFixture()
    {
        // Create a unique test data directory for this test run
        _testDataPath = Path.Combine(Path.GetTempPath(), "ignixa-e2e-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataPath);

        // Cache SQL connection string for consistent use throughout fixture lifecycle
        _sqlConnectionString = GetSqlConnectionString();
    }

    /// <summary>
    /// HTTP client for making requests to the test server.
    /// </summary>
    public HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// Search test harness initialized with cached capability statement.
    /// </summary>
    public SearchTestHarness Harness { get; private set; } = null!;

    /// <summary>
    /// Version-specific FHIR schema provider.
    /// </summary>
    public IFhirSchemaProvider SchemaProvider { get; private set; } = null!;

    /// <summary>
    /// FHIR version detected from server's capability statement.
    /// </summary>
    public FhirVersion FhirVersion { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Override configuration for tests
            // IMPORTANT: Use multi-tenant configuration pattern to override tenant storage
            var storageType = UseSqlServer ? "SqlEntityFramework" : "FileSystem";

            var configValues = new Dictionary<string, string?>
            {
                // Multi-tenancy mode
                ["Tenants:Mode"] = "Isolated",

                // System Partition (Tenant 0)
                ["Tenants:Configurations:0:TenantId"] = "0",
                ["Tenants:Configurations:0:DisplayName"] = "System Partition (Test)",
                ["Tenants:Configurations:0:FhirVersion"] = "4.0",
                ["Tenants:Configurations:0:IsActive"] = "true",
                ["Tenants:Configurations:0:IsSystemPartition"] = "true",
                ["Tenants:Configurations:0:Storage:Type"] = storageType,
                ["Tenants:Configurations:0:Storage:BaseDirectory"] = Path.Combine(_testDataPath, "system"),
                ["Tenants:Configurations:0:Packages:EnableAutoLoad"] = "false",
                ["Tenants:Configurations:0:Packages:InheritConnectionStringFromTenant"] = "1",

                // Tenant 1
                ["Tenants:Configurations:1:TenantId"] = "1",
                ["Tenants:Configurations:1:DisplayName"] = "E2E Test Tenant",
                ["Tenants:Configurations:1:FhirVersion"] = "4.0",
                ["Tenants:Configurations:1:IsActive"] = "true",
                ["Tenants:Configurations:1:Storage:Type"] = storageType,
                ["Tenants:Configurations:1:Storage:BaseDirectory"] = Path.Combine(_testDataPath, "tenants", "1"),

                // Disable package preloading for faster test startup
                ["Tenants:Configurations:1:Packages:EnableAutoLoad"] = "false",
                ["Tenants:Configurations:1:Packages:PreloadPackages:0"] = null!,

                // Mark Tenant 2 as inactive to avoid loading
                ["Tenants:Configurations:2:IsActive"] = "false",

                // Disable authentication for E2E tests
                ["Authentication:Enabled"] = "false",

                // Disable authorization for E2E tests (allows unauthenticated access)
                ["Authorization:Enabled"] = "false",
                ["Authorization:RequireAuthentication"] = "false",

                // Use in-memory index for search
                ["Search:IndexType"] = "InMemory",

                // Disable external dependencies
                ["DurableTask:Provider"] = "FileSystem",
                ["BlobStorage:Provider"] = "Local",
                ["BlobStorage:RootDirectory"] = Path.Combine(_testDataPath, "blobs"),

                // Disable MCP for tests
                ["Experimental:Features:Mcp:Enabled"] = "false",

                // Disable terminology auto-import for faster test startup
                ["Experimental:Features:Terminology:EnableAutoImport"] = "false",

                // Disable transaction watcher for tests
                ["TransactionWatcher:Enabled"] = "false",

                // Disable eager loading of package search parameters (avoids SQL connection)
                ["SearchParameters:ConflictResolution:EagerLoadPackageSearchParameters"] = "false",

                // Enable EF Core SQL logging for debugging
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Information",

                // Set test environment
                ["ASPNETCORE_ENVIRONMENT"] = "Test"
            };

            // Add SQL connection strings only when using SQL Server
            if (UseSqlServer)
            {
                configValues["Tenants:Configurations:0:Storage:ConnectionString"] = _sqlConnectionString;
                configValues["Tenants:Configurations:1:Storage:ConnectionString"] = _sqlConnectionString;
            }

            config.AddInMemoryCollection(configValues);
        });

        builder.ConfigureServices(services =>
        {
            // Additional test-specific service configuration can go here
            // For example, mock external dependencies, override registrations, etc.
        });

        builder.UseEnvironment("Test");
    }

    public async Task InitializeAsync()
    {
        // Initialize SQL database if using SQL Server mode
        if (UseSqlServer)
        {
            await InitializeSqlDatabaseAsync();
        }

        // Create HTTP client and store for test access
        Client = CreateClient();

        // Sync base search parameters to database for SQL Server mode
        // This ensures search parameters like _tag, address-city, etc. are present
        // before tests run. Without this, searches will fail with "SearchParamId not found".
        if (UseSqlServer)
        {
            await SyncBaseSearchParametersAsync();
        }

        // Fetch /metadata once and cache it
        var metadataResponse = await Client.GetAsync("/metadata");
        metadataResponse.EnsureSuccessStatusCode();

        var metadataJson = await metadataResponse.Content.ReadAsStringAsync();
        var capability = JsonSourceNodeFactory.Parse<CapabilityStatementJsonNode>(metadataJson);

        // Parse FHIR version from capability statement
        FhirVersion = ParseFhirVersion(capability);

        // Create version-specific schema provider
        SchemaProvider = CreateSchemaProvider(FhirVersion);

        // Initialize SearchTestHarness with cached capability
        Harness = new SearchTestHarness(Client, SchemaProvider, capability);
    }

    private async Task InitializeSqlDatabaseAsync()
    {
        var dbName = ExtractDatabaseName(_sqlConnectionString);

        // Create database if not exists - replace "Database=" or "Initial Catalog=" with "Initial Catalog=master"
        var masterConnStr = Regex.Replace(
            _sqlConnectionString,
            @"(Database|Initial\s+Catalog)=[^;]+",
            "Initial Catalog=master",
            RegexOptions.IgnoreCase);
        await using var masterConn = new SqlConnection(masterConnStr);
        await masterConn.OpenAsync();

        await using var cmd = masterConn.CreateCommand();
        // CA2100 suppressed: dbName comes from test configuration (environment variable or generated GUID),
        // not user input. This is safe in test fixture context.
#pragma warning disable CA2100
        cmd.CommandText = $"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{dbName}') CREATE DATABASE [{dbName}]";
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Syncs base FHIR search parameters to the database.
    /// CRITICAL: SQL Server mode requires search parameters to be registered in the SearchParam table
    /// before searches can work. Without this, queries fail with "SearchParamId not found for parameter..."
    /// </summary>
    private async Task SyncBaseSearchParametersAsync()
    {
        // Get the search parameter definition manager from the application services
        // This contains all base FHIR search parameters from the pre-generated code
        var fhirVersionContext = Services.GetRequiredService<IFhirVersionContext>();
        var searchParamManager = fhirVersionContext.GetSearchParameterDefinitionManager(FhirVersion.R4);

        // Get all search parameter URLs from base spec
        var searchParamUrls = searchParamManager.AllSearchParameters
            .Where(sp => sp.Url is not null)
            .Select(sp => sp.Url!.ToString())
            .Distinct()
            .ToList();

        // Get the repository factory to access the reference data cache
        var repositoryFactory = Services.GetRequiredService<SqlEntityFrameworkRepositoryFactory>();

        // Get the reference data cache for tenant 1 (the E2E test tenant)
        var referenceDataCache = await repositoryFactory.GetSearchIndexReferenceCacheAsync(1, CancellationToken.None);

        // Sync search parameters to database
        var syncedCount = await referenceDataCache.SyncSearchParametersToDatabase(
            searchParamUrls,
            searchParamManager);

        Console.WriteLine($"Synced {syncedCount} base search parameters to database ({searchParamUrls.Count} total)");
    }

    private static string ExtractDatabaseName(string connectionString)
    {
        // Match both "Database=..." and "Initial Catalog=..." formats
        var match = Regex.Match(connectionString, @"(Database|Initial\s+Catalog)=([^;]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[2].Value : throw new InvalidOperationException("Database name not found in connection string");
    }

    public new async Task DisposeAsync()
    {
        // Cleanup test data directory
        try
        {
            if (Directory.Exists(_testDataPath))
            {
                Directory.Delete(_testDataPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }

        await base.DisposeAsync();
    }

    private static FhirVersion ParseFhirVersion(CapabilityStatementJsonNode capability)
    {
        var fhirVersionString = capability.FhirVersionString;
        return fhirVersionString switch
        {
            "1.0.2" => FhirVersion.Stu3,
            "4.0.1" => FhirVersion.R4,
            "4.3.0" => FhirVersion.R4B,
            "5.0.0" => FhirVersion.R5,
            "6.0.0-ballot2" => FhirVersion.R6,
            _ => throw new NotSupportedException($"FHIR version {fhirVersionString} not supported")
        };
    }

    private static IFhirSchemaProvider CreateSchemaProvider(FhirVersion version)
    {
        return version switch
        {
            FhirVersion.Stu3 => new STU3CoreSchemaProvider(),
            FhirVersion.R4 => new R4CoreSchemaProvider(),
            FhirVersion.R4B => new R4BCoreSchemaProvider(),
            FhirVersion.R5 => new R5CoreSchemaProvider(),
            FhirVersion.R6 => new R6CoreSchemaProvider(),
            _ => throw new NotSupportedException($"FHIR version {version} not supported")
        };
    }
}
