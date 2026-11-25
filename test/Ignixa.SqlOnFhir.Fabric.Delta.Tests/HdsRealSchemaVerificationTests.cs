// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using DeltaLake.Table;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// Checks the creator against the licensed Healthcare data solutions package when <c>IGNIXA_HDS_PACKAGE_PATH</c> points
/// at a local copy; skipped otherwise. The package is never modified, and assertion messages never include schema text,
/// so a failure cannot copy licensed content into test output.
/// </summary>
public sealed class HdsRealSchemaVerificationTests : IDisposable
{
    private const string PackagePathVariable = "IGNIXA_HDS_PACKAGE_PATH";

    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [SkippableTheory]
    [InlineData("Patient")]
    [InlineData("Observation")]
    public async Task GivenHdsPackage_WhenCreatingTheSilverTable_ThenVersionZeroMatchesTheTemplateExceptChangeFeedCasing(string resourceType)
    {
        var packageRoot = RequirePackageRoot();
        var contract = await FabricSilverContract.LoadAsync(packageRoot, resourceType, CancellationToken.None);
        var tablePath = _directory.Combine(resourceType);

        var outcome = await FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None);

        outcome.ShouldBe(FabricSilverTableOutcome.Created);
        var template = DeltaLogReader.Read(TemplateTablePath(packageRoot, resourceType));
        var created = DeltaLogReader.Read(tablePath);
        created.Protocol.GetRawText().ShouldBe("""{"minReaderVersion":1,"minWriterVersion":4}""");
        Canonical(template.Protocol).ShouldBe(Canonical(created.Protocol), "protocol differs from the template");
        string.Equals(created.SchemaString, template.SchemaString, StringComparison.Ordinal)
            .ShouldBeTrue("schemaString differs from the template (text deliberately not shown)");
        Canonical(template.MetaData.GetProperty("partitionColumns"))
            .ShouldBe(Canonical(created.MetaData.GetProperty("partitionColumns")), "partitionColumns differ from the template");
        Canonical(template.MetaData.GetProperty("format"))
            .ShouldBe(Canonical(created.MetaData.GetProperty("format")), "format differs from the template");
        created.MetaDataKeys.ShouldBe(template.MetaDataKeys);
        created.Configuration.Select(property => property.Key)
            .SequenceEqual(template.Configuration.Select(property => property.Key), StringComparer.Ordinal)
            .ShouldBeTrue("configuration keys or their order differ from the template");
        foreach (var (key, value) in template.Configuration)
        {
            var expected = key == "delta.enableChangeDataFeed" ? "true" : value;
            string.Equals(created.Configuration.Single(property => property.Key == key).Value, expected, StringComparison.Ordinal)
                .ShouldBeTrue($"configuration value of {key} differs from the template");
        }

        // Real templates are already in the engine's compact form, so validation sees the template text unchanged.
        using var engine = new DeltaEngine(EngineOptions.Default);
        using var table = await engine.LoadTableAsync(new TableOptions { TableLocation = tablePath }, CancellationToken.None);
        string.Equals(table.Metadata().SchemaString, template.SchemaString, StringComparison.Ordinal)
            .ShouldBeTrue("the engine's schema text differs from the template (text deliberately not shown)");
    }

    [SkippableTheory]
    [InlineData("Patient")]
    [InlineData("Observation")]
    public async Task GivenTableCreatedFromTheHdsTemplateAsIs_WhenEnsuringTable_ThenTheChangeFeedCasingIsRejected(string resourceType)
    {
        var packageRoot = RequirePackageRoot();
        var contract = await FabricSilverContract.LoadAsync(packageRoot, resourceType, CancellationToken.None);
        var tablePath = _directory.Combine(resourceType);
        var logCopy = Directory.CreateDirectory(Path.Combine(tablePath, "_delta_log")).FullName;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(TemplateTablePath(packageRoot, resourceType), "_delta_log")))
        {
            File.Copy(file, Path.Combine(logCopy, Path.GetFileName(file)));
        }

        var exception = await Should.ThrowAsync<FabricSilverTableMismatchException>(
            () => FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None));

        exception.Aspect.ShouldBe(FabricSilverTableAspect.ChangeDataFeed);
        exception.PropertyName.ShouldBe("delta.enableChangeDataFeed");
    }

    private static string RequirePackageRoot()
    {
        var packageRoot = Environment.GetEnvironmentVariable(PackagePathVariable);
        Skip.If(string.IsNullOrWhiteSpace(packageRoot), $"{PackagePathVariable} is not set; real-schema verification skipped.");
        return packageRoot!;
    }

    private static string TemplateTablePath(string packageRoot, string resourceType) =>
        Path.Combine(packageRoot, "HDS.SourceCode", "hds-build-artifacts", "healthcare-tables", "1.4.0", "Silver", resourceType);

    private static string Canonical(JsonElement element) => JsonSerializer.Serialize(element);
}
