// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization;
using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.SqlOnFhir.Writers.Tests;

public sealed class ParquetFileWriterTests : IDisposable
{
    private readonly string _tempDir;

    public ParquetFileWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"parquet-tests-{Guid.NewGuid()}");
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
    public async Task WriteRow_ShouldCreateParquetFile()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDir, "test.parquet");
        var schema = SchemaExtractorTests.CreateSimpleParquetSchema();
        var columnTypeMap = new Dictionary<string, string>
        {
            ["id"] = "STRING",
            ["name"] = "STRING",
            ["age"] = "INTEGER"
        };

        var writer = new ParquetFileWriter(outputPath, schema, NullLogger.Instance, columnTypeMap);

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
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task WriteMultipleRows_ShouldBatchCorrectly()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDir, "batch-test.parquet");
        var schema = SchemaExtractorTests.CreateSimpleParquetSchema();
        var columnTypeMap = new Dictionary<string, string>
        {
            ["id"] = "STRING",
            ["name"] = "STRING",
            ["age"] = "INTEGER"
        };

        var writer = new ParquetFileWriter(outputPath, schema, NullLogger.Instance, columnTypeMap, rowsPerBatch: 5);

        // Act - write 12 rows (should create 3 batches: 5, 5, 2)
        var rows = Enumerable.Range(1, 12).Select(i => new Dictionary<string, object?>
        {
            ["id"] = $"id-{i}",
            ["name"] = $"Person {i}",
            ["age"] = 20 + i
        });

        await writer.WriteRowsAsync(rows);
        await writer.FlushAsync();
        await writer.DisposeAsync();

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        writer.BytesWritten.Should().BeGreaterThan(0);
    }
}
