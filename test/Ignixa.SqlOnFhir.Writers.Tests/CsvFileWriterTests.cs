// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.SqlOnFhir.Writers.Tests;

public sealed class CsvFileWriterTests : IDisposable
{
    private readonly string _tempDir;

    public CsvFileWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"csv-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task WriteRow_ShouldCreateCsvFileWithHeader()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDir, "test.csv");
        var writer = new CsvFileWriter(outputPath, NullLogger.Instance);

        // Act
        var row1 = new Dictionary<string, object?>
        {
            ["id"] = "123",
            ["name"] = "John Doe",
            ["age"] = 30
        };

        var row2 = new Dictionary<string, object?>
        {
            ["id"] = "456",
            ["name"] = "Jane Smith",
            ["age"] = 25
        };

        await writer.WriteRowAsync(row1);
        await writer.WriteRowAsync(row2);
        await writer.FlushAsync();
        await writer.DisposeAsync();

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(outputPath);
        lines.Should().HaveCount(3); // Header + 2 rows
        lines[0].Should().Be("id,name,age"); // Header
        lines[1].Should().Contain("123");
        lines[1].Should().Contain("John Doe");
        lines[2].Should().Contain("456");
        lines[2].Should().Contain("Jane Smith");
    }

    [Fact]
    public async Task WriteRow_ShouldEscapeValuesWithCommas()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDir, "escape-test.csv");
        var writer = new CsvFileWriter(outputPath, NullLogger.Instance);

        // Act
        var row = new Dictionary<string, object?>
        {
            ["id"] = "123",
            ["name"] = "Doe, John",
            ["city"] = "New York, NY"
        };

        await writer.WriteRowAsync(row);
        await writer.FlushAsync();
        await writer.DisposeAsync();

        // Assert
        var lines = await File.ReadAllLinesAsync(outputPath);
        lines[1].Should().Contain("\"Doe, John\"");
        lines[1].Should().Contain("\"New York, NY\"");
    }

    [Fact]
    public async Task WriteMultipleRows_ShouldTrackRowCount()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDir, "count-test.csv");
        var writer = new CsvFileWriter(outputPath, NullLogger.Instance);

        // Act
        var rows = Enumerable.Range(1, 10).Select(i => new Dictionary<string, object?>
        {
            ["id"] = $"id-{i}",
            ["value"] = i * 10
        });

        await writer.WriteRowsAsync(rows);
        await writer.FlushAsync();

        // Assert
        writer.RowsWritten.Should().Be(10);
        await writer.DisposeAsync();
    }
}
