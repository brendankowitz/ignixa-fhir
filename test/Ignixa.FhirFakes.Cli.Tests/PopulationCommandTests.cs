// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Cli.Commands;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Cli.Tests;

public class PopulationCommandTests
{
    [Fact]
    public async Task GivenZeroPatients_WhenGeneratingCombinedBundle_ThenExitsWithCode1()
    {
        // Arrange
        Environment.ExitCode = 0;
        var tempDir = CreateTempDirectory();

        try
        {
            var schemaProvider = new R4CoreSchemaProvider();
            var command = PopulationCommand.Create(schemaProvider, "r4");
            var args = new[] { "--out", tempDir, "--count", "0", "--from", "TX" };

            // Act
            var parseResult = command.Parse(args);
            await parseResult.InvokeAsync();

            // Assert
            Environment.ExitCode.ShouldBe(1);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task GivenZeroPatients_WhenGeneratingWithResolvedReferences_ThenExitsWithCode1()
    {
        // Arrange
        Environment.ExitCode = 0;
        var tempDir = CreateTempDirectory();

        try
        {
            var schemaProvider = new R4CoreSchemaProvider();
            var command = PopulationCommand.Create(schemaProvider, "r4");
            var args = new[] { "--out", tempDir, "--count", "0", "--from", "TX", "--resolved-references" };

            // Act
            var parseResult = command.Parse(args);
            await parseResult.InvokeAsync();

            // Assert
            Environment.ExitCode.ShouldBe(1);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task GivenZeroPatients_WhenGeneratingNdjson_ThenExitsWithCode1()
    {
        // Arrange
        Environment.ExitCode = 0;
        var tempDir = CreateTempDirectory();

        try
        {
            var schemaProvider = new R4CoreSchemaProvider();
            var command = PopulationCommand.Create(schemaProvider, "r4");
            var args = new[] { "--out", tempDir, "--count", "0", "--from", "TX", "--ndjson" };

            // Act
            var parseResult = command.Parse(args);
            await parseResult.InvokeAsync();

            // Assert
            Environment.ExitCode.ShouldBe(1);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task GivenValidPatients_WhenGeneratingCombinedBundle_ThenExitsSuccessfully()
    {
        // Arrange
        Environment.ExitCode = 0;
        var tempDir = CreateTempDirectory();

        try
        {
            var schemaProvider = new R4CoreSchemaProvider();
            var command = PopulationCommand.Create(schemaProvider, "r4");
            var args = new[] { "--out", tempDir, "--count", "1", "--from", "TX" };

            // Act
            var parseResult = command.Parse(args);
            await parseResult.InvokeAsync();

            // Assert
            Environment.ExitCode.ShouldBe(0);
            Directory.GetFiles(tempDir).ShouldNotBeEmpty();
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task GivenValidPatients_WhenGeneratingWithResolvedReferences_ThenExitsSuccessfully()
    {
        // Arrange
        Environment.ExitCode = 0;
        var tempDir = CreateTempDirectory();

        try
        {
            var schemaProvider = new R4CoreSchemaProvider();
            var command = PopulationCommand.Create(schemaProvider, "r4");
            var args = new[] { "--out", tempDir, "--count", "1", "--from", "TX", "--resolved-references" };

            // Act
            var parseResult = command.Parse(args);
            await parseResult.InvokeAsync();

            // Assert
            Environment.ExitCode.ShouldBe(0);
            Directory.GetFiles(tempDir).ShouldNotBeEmpty();
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task GivenValidPatients_WhenGeneratingNdjson_ThenExitsSuccessfully()
    {
        // Arrange
        Environment.ExitCode = 0;
        var tempDir = CreateTempDirectory();

        try
        {
            var schemaProvider = new R4CoreSchemaProvider();
            var command = PopulationCommand.Create(schemaProvider, "r4");
            var args = new[] { "--out", tempDir, "--count", "1", "--from", "TX", "--ndjson" };

            // Act
            var parseResult = command.Parse(args);
            await parseResult.InvokeAsync();

            // Assert
            Environment.ExitCode.ShouldBe(0);
            Directory.GetFiles(tempDir).ShouldNotBeEmpty();
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "fhir-fakes-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void CleanupTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
