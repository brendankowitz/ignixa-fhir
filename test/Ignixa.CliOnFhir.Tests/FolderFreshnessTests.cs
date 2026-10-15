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
    private static readonly TimeSpan PastTheReuseWindow = FolderSession.StaleFilesReuseWindow + TimeSpan.FromSeconds(1);

    [Fact]
    public void GivenNothingChangedSinceTheWalk_WhenInspected_ThenStaleFilesIsZero()
    {
        using TestFolder folder = CreateFolder();
        FolderSession session = Open(folder, new ManualTimeProvider());

        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(0);
    }

    [Fact]
    public void GivenFileEditedAfterTheSessionCataloguedIt_WhenInspectedAgain_ThenStaleFilesCountsIt()
    {
        using TestFolder folder = CreateFolder();
        var clock = new ManualTimeProvider();
        FolderSession session = Open(folder, clock);
        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(0);

        folder.Write("a.json", """{"resourceType":"Patient","id":"a","active":true}""");
        clock.Advance(PastTheReuseWindow);
        ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(session);

        envelope.Freshness.StaleFiles.ShouldBe(1);
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenFileOnlyTouched_WhenInspected_ThenStaleFilesCountsIt()
    {
        using TestFolder folder = CreateFolder();
        FolderSession session = Open(folder, new ManualTimeProvider());
        session.Catalog.ShouldNotBeNull();
        string path = Path.Combine(folder.Path, "a.json");

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));

        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(1);
    }

    [Fact]
    public void GivenFilesAddedAndRemovedAfterTheWalk_WhenInspected_ThenBothAreCountedAndUnsupportedFilesAreNot()
    {
        using TestFolder folder = CreateFolder();
        FolderSession session = Open(folder, new ManualTimeProvider());
        session.Catalog.ShouldNotBeNull();

        File.Delete(Path.Combine(folder.Path, "b.ndjson"));
        folder.Write("new.json", """{"resourceType":"Patient","id":"n"}""");
        folder.Write("sub/deeper.ndjson", """{"resourceType":"Patient","id":"d"}""");
        folder.Write("notes.txt", "not data");

        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(3);
    }

    [Fact]
    public void GivenCountMadeJustBefore_WhenInspectedAgainWithinTheWindow_ThenTheCountIsReusedUntilTheWindowPasses()
    {
        using TestFolder folder = CreateFolder();
        var clock = new ManualTimeProvider();
        FolderSession session = Open(folder, clock);
        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(0);

        folder.Write("a.json", """{"resourceType":"Patient","id":"a","active":true}""");
        clock.Advance(FolderSession.StaleFilesReuseWindow / 2);
        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(0);

        clock.Advance(FolderSession.StaleFilesReuseWindow);
        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(1);
    }

    [Fact]
    public void GivenFolderDeletedAfterCataloguing_WhenInspected_ThenTheCallSucceedsWithUnknownFreshnessAndAWarning()
    {
        using TestFolder folder = CreateFolder();
        var clock = new ManualTimeProvider();
        FolderSession session = Open(folder, clock);
        FolderInspector.Inspect(session).Freshness.StaleFiles.ShouldBe(0);
        Directory.Delete(folder.Path, recursive: true);
        clock.Advance(PastTheReuseWindow);

        ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(session);

        envelope.Facts.ResourceCount.ShouldBe(2);
        envelope.Freshness.StaleFiles.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("freshness-unavailable");
        issue.Severity.ShouldBe("warning");
        issue.Diagnostics.ShouldContain("staleFiles is unknown");
    }

    [Fact]
    public void GivenRecountCancelled_WhenInspected_ThenItIsCancelledRatherThanReportedAsUnknown()
    {
        using TestFolder folder = CreateFolder();
        FolderSession session = Open(folder, new ManualTimeProvider());
        session.Catalog.ShouldNotBeNull();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Should.Throw<OperationCanceledException>(() => FolderInspector.Inspect(session, cancellationToken: cancellation.Token));
    }

    private static FolderSession Open(TestFolder folder, TimeProvider clock) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: clock);

    private static TestFolder CreateFolder() => new TestFolder()
        .Write("a.json", """{"resourceType":"Patient","id":"a"}""")
        .Write("b.ndjson", """{"resourceType":"Patient","id":"b"}""");
}
