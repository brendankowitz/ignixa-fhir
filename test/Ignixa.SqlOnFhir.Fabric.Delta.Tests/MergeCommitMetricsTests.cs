// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// The commit file a MERGE result is read from is external data; anything but a MERGE commit carrying both
/// non-negative row counts must be refused rather than reported as zero.
/// </summary>
public class MergeCommitMetricsTests
{
    private const string AddAction = """{"add":{"path":"part-0.parquet","size":1,"modificationTime":0,"dataChange":true}}""";

    [Fact]
    public async Task GivenAMergeCommit_WhenRead_ThenTheInsertedAndUpdatedCountsAreReturned()
    {
        using var root = new TemporaryDirectory();
        await WriteCommitAsync(
            root.Path,
            AddAction,
            """{"commitInfo":{"operation":"MERGE","operationMetrics":{"num_target_rows_inserted":3,"num_target_rows_updated":2,"num_target_rows_copied":7}}}""");

        var metrics = await MergeCommitMetrics.ReadAsync(root.Path, 1, sourceRows: 5, CancellationToken.None);

        metrics.ShouldBe(new MergeCommitMetrics(3, 2));
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("\"commitInfo\"")]
    [InlineData("""{"commitInfo":1}""")]
    [InlineData("""{"commitInfo":["MERGE"]}""")]
    [InlineData("""{"protocol":{"minReaderVersion":1,"minWriterVersion":4}}""")]
    [InlineData("""{"commitInfo":{"operationMetrics":{"num_target_rows_inserted":1,"num_target_rows_updated":1}}}""")]
    [InlineData("""{"commitInfo":{"operation":1,"operationMetrics":{"num_target_rows_inserted":1,"num_target_rows_updated":1}}}""")]
    [InlineData("""{"commitInfo":{"operation":"WRITE","operationMetrics":{"num_target_rows_inserted":1,"num_target_rows_updated":1}}}""")]
    [InlineData("""{"commitInfo":{"operation":"MERGE"}}""")]
    [InlineData("""{"commitInfo":{"operation":"MERGE","operationMetrics":"none"}}""")]
    [InlineData("""{"commitInfo":{"operation":"MERGE","operationMetrics":{"num_target_rows_inserted":1}}}""")]
    [InlineData("""{"commitInfo":{"operation":"MERGE","operationMetrics":{"num_target_rows_inserted":"1","num_target_rows_updated":1}}}""")]
    [InlineData("""{"commitInfo":{"operation":"MERGE","operationMetrics":{"num_target_rows_inserted":1.5,"num_target_rows_updated":1}}}""")]
    [InlineData("""{"commitInfo":{"operation":"MERGE","operationMetrics":{"num_target_rows_inserted":1,"num_target_rows_updated":-1}}}""")]
    public async Task GivenACommitWithoutUsableMergeMetrics_WhenRead_ThenItIsRefusedNamingTheVersion(string action)
    {
        using var root = new TemporaryDirectory();
        await WriteCommitAsync(root.Path, AddAction, action);

        var exception = await Should.ThrowAsync<InvalidDataException>(() => MergeCommitMetrics.ReadAsync(root.Path, 1, sourceRows: 5, CancellationToken.None));

        exception.Message.ShouldContain("table version 1 ");
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(0, 5)]
    [InlineData(long.MaxValue, 1)]
    [InlineData(1, long.MaxValue)]
    public async Task GivenMetricsCountingMoreRowsThanTheMergeWasGiven_WhenRead_ThenTheyAreRefused(long inserted, long updated)
    {
        using var root = new TemporaryDirectory();
        await WriteCommitAsync(
            root.Path,
            AddAction,
            "{\"commitInfo\":{\"operation\":\"MERGE\",\"operationMetrics\":{\"num_target_rows_inserted\":" + inserted + ",\"num_target_rows_updated\":" + updated + "}}}");

        var exception = await Should.ThrowAsync<InvalidDataException>(() => MergeCommitMetrics.ReadAsync(root.Path, 1, sourceRows: 4, CancellationToken.None));

        exception.Message.ShouldContain("table version 1 ");
    }

    private static async Task WriteCommitAsync(string table, params string[] actions)
    {
        var log = Directory.CreateDirectory(Path.Combine(table, "_delta_log")).FullName;
        await File.WriteAllLinesAsync(Path.Combine(log, "00000000000000000001.json"), actions);
    }
}
