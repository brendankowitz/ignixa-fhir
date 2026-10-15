// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Inspection;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

public class FolderFreshnessTests
{
    [Fact]
    public void GivenNothingChangedSinceTheWalk_WhenInspected_ThenStaleFilesIsZero()
    {
        using TestFolder folder = CreateFolder();
        FolderSession session = Open(folder);

        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(0);
    }

    [Fact]
    public void GivenFileEditedAfterTheSessionCataloguedIt_WhenInspectedAgain_ThenStaleFilesCountsIt()
    {
        using TestFolder folder = CreateFolder();
        FolderSession session = Open(folder);
        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(0);

        folder.Write("a.json", """{"resourceType":"Patient","id":"a","active":true}""");
        ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(session);

        envelope.Freshness.StaleFiles.ShouldBe(1);
    }

    [Fact]
    public void GivenFileOnlyTouched_WhenInspected_ThenStaleFilesCountsIt()
    {
        using TestFolder folder = CreateFolder();
        FolderSession session = Open(folder);
        session.Catalog.ShouldNotBeNull();
        string path = Path.Combine(folder.Path, "a.json");

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));

        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(1);
    }

    [Fact]
    public void GivenFilesAddedAndRemovedAfterTheWalk_WhenInspected_ThenBothAreCountedAndUnsupportedFilesAreNot()
    {
        using TestFolder folder = CreateFolder();
        FolderSession session = Open(folder);
        session.Catalog.ShouldNotBeNull();

        File.Delete(Path.Combine(folder.Path, "b.ndjson"));
        folder.Write("new.json", """{"resourceType":"Patient","id":"n"}""");
        folder.Write("sub/deeper.ndjson", """{"resourceType":"Patient","id":"d"}""");
        folder.Write("notes.txt", "not data");

        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(3);
    }

    private static FolderSession Open(TestFolder folder) => new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);

    private static TestFolder CreateFolder() => new TestFolder()
        .Write("a.json", """{"resourceType":"Patient","id":"a"}""")
        .Write("b.ndjson", """{"resourceType":"Patient","id":"b"}""");
}
