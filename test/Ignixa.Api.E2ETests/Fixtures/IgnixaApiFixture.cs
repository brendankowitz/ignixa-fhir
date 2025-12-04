// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Api.E2ETests.Infrastructure;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ignixa.Api.E2ETests.Fixtures;

/// <summary>
/// Test fixture for E2E tests using WebApplicationFactory.
/// Configures the Ignixa API with in-memory storage for testing.
/// Program is public to support WebApplicationFactory in tests.
/// </summary>
public class IgnixaApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _testDataPath;

    public IgnixaApiFixture()
    {
        // Create a unique test data directory for this test run
        _testDataPath = Path.Combine(Path.GetTempPath(), "ignixa-e2e-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataPath);
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
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Multi-tenancy mode
                ["Tenants:Mode"] = "Isolated",

                // System Partition (Tenant 0) - FileSystem for tests
                ["Tenants:Configurations:0:TenantId"] = "0",
                ["Tenants:Configurations:0:DisplayName"] = "System Partition (Test)",
                ["Tenants:Configurations:0:FhirVersion"] = "4.0",
                ["Tenants:Configurations:0:IsActive"] = "true",
                ["Tenants:Configurations:0:IsSystemPartition"] = "true",
                ["Tenants:Configurations:0:Storage:Type"] = "SqlEntityFramework",
                ["Tenants:Configurations:0:Storage:BaseDirectory"] = Path.Combine(_testDataPath, "system"),
                ["Tenants:Configurations:0:Packages:EnableAutoLoad"] = "false",
                ["Tenants:Configurations:0:Packages:InheritConnectionStringFromTenant"] = "1",

                // Tenant 1 - FileSystem for tests (overrides SQL from appsettings.json)
                ["Tenants:Configurations:1:TenantId"] = "1",
                ["Tenants:Configurations:1:DisplayName"] = "E2E Test Tenant",
                ["Tenants:Configurations:1:FhirVersion"] = "4.0",
                ["Tenants:Configurations:1:IsActive"] = "true",
                ["Tenants:Configurations:1:Storage:Type"] = "SqlEntityFramework",
                ["Tenants:Configurations:1:Storage:ConnectionString"] = "server=(local);Initial Catalog=FHIR_R4;Integrated Security=true;TrustServerCertificate=true",
                ["Tenants:Configurations:1:Storage:BaseDirectory"] = Path.Combine(_testDataPath, "tenants", "1"),

                // Disable package preloading for faster test startup
                ["Tenants:Configurations:1:Packages:EnableAutoLoad"] = "false",
                ["Tenants:Configurations:1:Packages:PreloadPackages:0"] = null!,

                // Mark Tenant 2 as inactive to avoid loading
                ["Tenants:Configurations:2:IsActive"] = "false",

                // Disable authentication for E2E tests
                ["Authentication:Enabled"] = "false",

                // Use in-memory index for search
                ["Search:IndexType"] = "InMemory",

                // Disable external dependencies
                ["DurableTask:Provider"] = "FileSystem",
                ["BlobStorage:Provider"] = "Local",
                ["BlobStorage:RootDirectory"] = Path.Combine(_testDataPath, "blobs"),

                // Disable MCP for tests
                ["Mcp:Enabled"] = "false",

                // Disable terminology auto-import for faster test startup
                ["Terminology:EnableAutoImport"] = "false",

                // Disable transaction watcher for tests
                ["TransactionWatcher:Enabled"] = "false",

                // Disable eager loading of package search parameters (avoids SQL connection)
                ["SearchParameters:ConflictResolution:EagerLoadPackageSearchParameters"] = "false",

                // Set test environment
                ["ASPNETCORE_ENVIRONMENT"] = "Test"
            });
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
        // Create HTTP client and store for test access
        Client = CreateClient();

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
