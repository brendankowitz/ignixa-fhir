// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ignixa.Api.E2ETests.Fixtures;

/// <summary>
/// Test fixture for E2E tests using WebApplicationFactory.
/// Configures the Ignixa API with in-memory storage for testing.
/// Suppresses CS0060 because Program is internal but accessible via InternalsVisibleTo.
/// </summary>
internal class IgnixaApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _testDataPath;

    public IgnixaApiFixture()
    {
        // Create a unique test data directory for this test run
        _testDataPath = Path.Combine(Path.GetTempPath(), "ignixa-e2e-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Override configuration for tests
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Use filesystem storage with test directory
                ["DataLayer:Type"] = "FileSystem",
                ["DataLayer:FileSystem:BasePath"] = _testDataPath,

                // Disable authentication for E2E tests
                ["Authentication:Enabled"] = "false",

                // Use in-memory index for search
                ["Search:IndexType"] = "InMemory",

                // Disable external dependencies
                ["DurableTask:Enabled"] = "false",
                ["Azure:Storage:Enabled"] = "false",

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

    public Task InitializeAsync()
    {
        // Perform any async initialization here
        return Task.CompletedTask;
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
}
