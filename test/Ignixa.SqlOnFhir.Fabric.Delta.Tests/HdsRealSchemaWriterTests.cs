// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// Writes invented resources into tables created from the licensed Healthcare data solutions contracts when
/// <c>IGNIXA_HDS_PACKAGE_PATH</c> points at a local copy; skipped otherwise. Only the package's creation logs are read;
/// no sample data is used, and assertion messages never include schema text or row values.
/// </summary>
public sealed class HdsRealSchemaWriterTests : IDisposable
{
    private const string PackagePathVariable = "IGNIXA_HDS_PACKAGE_PATH";
    private const string Earlier = "2025-01-01T00:00:00Z";
    private const string Later = "2025-06-01T00:00:00Z";

    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [SkippableTheory]
    [InlineData("Patient")]
    [InlineData("Observation")]
    public async Task GivenAnHdsContract_WhenInsertingUpdatingAndReplaying_ThenTheEngineAndTheIndependentReaderAgree(string resourceType)
    {
        var packageRoot = Environment.GetEnvironmentVariable(PackagePathVariable);
        Skip.If(string.IsNullOrWhiteSpace(packageRoot), $"{PackagePathVariable} is not set; real-schema verification skipped.");
        var contract = await FabricSilverContract.LoadAsync(packageRoot!, resourceType, CancellationToken.None);
        Func<int, string, ResourceJsonNode> resource = resourceType == "Patient" ? WideSilverResources.Patient : WideSilverResources.Observation;
        var prefix = resourceType == "Patient" ? "pat" : "obs";
        var tablePath = _directory.Combine(resourceType);

        var inserted = await WriteAsync(tablePath, contract, resource(0, Earlier), resource(1, Earlier), resource(2, Earlier));
        inserted.Outcome.ShouldBe(FabricSilverTableOutcome.Created);
        inserted.CommittedVersion.ShouldBe(1);
        inserted.InsertedRows.ShouldBe(3);
        var before = await RowsByIdAsync(tablePath);

        ResourceJsonNode[] second = [resource(0, Later), resource(1, Earlier), resource(3, Earlier)];
        var changed = await WriteAsync(tablePath, contract, second);
        changed.CommittedVersion.ShouldBe(2);
        (changed.InsertedRows, changed.UpdatedRows, changed.UnchangedRows).ShouldBe((1L, 1L, 1L));

        var replayed = await WriteAsync(tablePath, contract, second);
        replayed.CommittedVersion.ShouldBeNull();
        replayed.UnchangedRows.ShouldBe(3);

        var expectedIds = Enumerable.Range(0, 4).Select(key => SyntheticPatients.SilverId(resourceType, $"{prefix}-{key}")).Order(StringComparer.Ordinal).ToArray();
        var (version, engineIds) = await EngineTableReader.ReadIdsAsync(tablePath);
        version.ShouldBe(2);
        engineIds.ShouldBe(expectedIds);
        var after = await RowsByIdAsync(tablePath);
        after.Keys.Order(StringComparer.Ordinal).ShouldBe(expectedIds);
        JsonNode.DeepEquals(after[Id(resourceType, prefix, 0)], before[Id(resourceType, prefix, 0)]).ShouldBeFalse("the updated row was not replaced");
        JsonNode.DeepEquals(after[Id(resourceType, prefix, 1)], before[Id(resourceType, prefix, 1)]).ShouldBeTrue("the unchanged row was rewritten");
        JsonNode.DeepEquals(after[Id(resourceType, prefix, 2)], before[Id(resourceType, prefix, 2)]).ShouldBeTrue("an untouched row was rewritten");
    }

    private static async Task<FabricSilverTableResult> WriteAsync(string tablePath, FabricSilverContract contract, params ResourceJsonNode[] resources)
    {
        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(tablePath, contract, resources)], WriterContracts.Ingestion(), CancellationToken.None);
        return result.Tables.ShouldHaveSingleItem();
    }

    private static async Task<Dictionary<string, JsonObject>> RowsByIdAsync(string tablePath) =>
        (await DeltaParquetReader.ReadLiveRowsAsync(tablePath)).ToDictionary(row => (string)row["id"]!, StringComparer.Ordinal);

    private static string Id(string resourceType, string prefix, int key) => SyntheticPatients.SilverId(resourceType, $"{prefix}-{key}");
}
