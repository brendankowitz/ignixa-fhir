// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Commands;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Inspection;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

public class IndexCacheTests
{
    private static readonly string[] FixtureTypes = ["Encounter", "Observation", "Patient"];

    [Fact]
    public void GivenIndexBuiltAndPersisted_WhenReloadedInANewSession_ThenEveryEntryDiagnosticAndParameterIsIdentical()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession writer = Open(folder, cache);
        FolderSession reader = Open(folder, cache);

        foreach (string type in FixtureTypes)
        {
            ResourceTypeIndex fresh = writer.GetIndex(type);
            ResourceTypeIndex loaded = reader.GetIndex(type);

            fresh.FromCache.ShouldBeFalse();
            loaded.FromCache.ShouldBeTrue();
            loaded.Resources.ShouldNotBeEmpty();
            SearchIndexCanon.Describe(loaded).ShouldBe(SearchIndexCanon.Describe(fresh));
            loaded.Diagnostics.ShouldBe(fresh.Diagnostics);
            loaded.BuiltAt.ShouldBe(fresh.BuiltAt);
            loaded.IndexEntryCount.ShouldBe(fresh.IndexEntryCount);
            loaded.Resources.Select(resource => resource.Entry).ShouldBe(fresh.Resources.Select(resource => resource.Entry));
        }

        reader.CacheIssues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenFixtureCoveringEveryParameterType_WhenIndexed_ThenTheRoundTripActuallyExercisesThem()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession session = Open(folder, cache);

        string canon = string.Join('\n', FixtureTypes.SelectMany(type => SearchIndexCanon.Describe(session.GetIndex(type))));

        // string, token (with mixed-case text), date (with a half-hour negative offset), reference, quantity, composite
        canon.ShouldContain("String[\"Smith\"");
        canon.ShouldContain("text=\"Body Height\"");
        canon.ShouldContain("Date[");
        canon.ShouldContain("Reference[InternalOrExternal");
        canon.ShouldContain("Quantity[");
        canon.ShouldContain("Composite[");
        canon.ShouldContain("text=\"Annual Check-up Visit\"");
        canon.ShouldContain("-03:-30");
    }

    [Fact]
    public void GivenIndexWithExtractionFailures_WhenReloaded_ThenTheDiagnosticsRoundTrip()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        ResourceTypeIndex fresh = Open(folder, cache).GetIndex("Observation");
        fresh.Diagnostics.ShouldNotBeEmpty();

        ResourceTypeIndex loaded = Open(folder, cache).GetIndex("Observation");

        loaded.FromCache.ShouldBeTrue();
        loaded.Diagnostics.ShouldBe(fresh.Diagnostics);
    }

    [Fact]
    public void GivenCachedIndex_WhenReloaded_ThenEveryEntryPointsAtTheSessionsOwnParameterDefinition()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession writer = Open(folder, cache);
        FolderSession reader = Open(folder, cache);

        foreach (string type in FixtureTypes)
        {
            foreach (IndexedResource resource in writer.GetIndex(type).Resources)
            {
                foreach (var entry in resource.Index)
                {
                    entry.SearchParameter.ShouldBeSameAs(writer.Definitions.GetSearchParameter(type, entry.SearchParameter.Code));
                }
            }

            ResourceTypeIndex loaded = reader.GetIndex(type);
            loaded.FromCache.ShouldBeTrue();
            foreach (IndexedResource resource in loaded.Resources)
            {
                foreach (var entry in resource.Index)
                {
                    entry.SearchParameter.ShouldBeSameAs(reader.Definitions.GetSearchParameter(type, entry.SearchParameter.Code));
                }
            }
        }
    }

    [Fact]
    public void GivenUnchangedFolder_WhenSecondSessionOpens_ThenItLoadsFromCacheWithoutIssues()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        Open(folder, cache).GetIndex("Patient");
        FolderSession second = Open(folder, cache);

        ResourceTypeIndex index = second.GetIndex("Patient");

        index.FromCache.ShouldBeTrue();
        index.CacheWriteMilliseconds.ShouldBeNull();
        second.CacheIssues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTypeNeverPersisted_WhenSecondSessionRequestsIt_ThenItIsBuiltAndPersistedWithoutIssues()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        Open(folder, cache).GetIndex("Patient");
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeTrue();
        ResourceTypeIndex encounter = second.GetIndex("Encounter");

        encounter.FromCache.ShouldBeFalse();
        encounter.CacheWriteMilliseconds.ShouldNotBeNull();
        second.CacheIssues.ShouldBeEmpty();
        Open(folder, cache).GetIndex("Encounter").FromCache.ShouldBeTrue();
    }

    [Fact]
    public void GivenFileContentChanged_WhenSessionOpens_ThenIndexIsRebuiltAndTheChangeIsReported()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        Open(folder, cache).GetIndex("Patient");
        folder.Write("patient.json", """{"resourceType":"Patient","id":"p1","name":[{"family":"Jones"}]}""");
        FolderSession second = Open(folder, cache);

        ResourceTypeIndex index = second.GetIndex("Patient");

        index.FromCache.ShouldBeFalse();
        index.Resources.ShouldHaveSingleItem().Index.Select(entry => SearchIndexCanon.Describe(entry.Value)).ShouldContain(value => value.Contains("Jones", StringComparison.Ordinal));
        IssueItem issue = second.CacheIssues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("index-cache-rebuilt");
        issue.Severity.ShouldBe("information");
        issue.Diagnostics.ShouldContain("1 of the folder's files changed");
        issue.Diagnostics.ShouldContain("1 modified");
        Open(folder, cache).GetIndex("Patient").FromCache.ShouldBeTrue();
    }

    [Fact]
    public void GivenFileOnlyTouched_WhenSessionOpens_ThenIndexIsRebuilt()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        Open(folder, cache).GetIndex("Patient");
        string path = Path.Combine(folder.Path, "patient.json");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();

        second.CacheIssues.ShouldHaveSingleItem().Diagnostics.ShouldContain("1 modified");
    }

    [Fact]
    public void GivenFilesAddedAndRemoved_WhenSessionOpens_ThenIndexIsRebuiltAndBothAreCounted()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        Open(folder, cache).GetIndex("Patient");
        File.Delete(Path.Combine(folder.Path, "encounters.ndjson"));
        folder.Write("another.json", """{"resourceType":"Patient","id":"p2"}""");
        FolderSession second = Open(folder, cache);

        ResourceTypeIndex index = second.GetIndex("Patient");

        index.FromCache.ShouldBeFalse();
        index.Resources.Count.ShouldBe(2);
        second.CacheIssues.ShouldHaveSingleItem().Diagnostics.ShouldContain("2 of the folder's files changed since the cache was written (1 added, 1 removed, 0 modified)");
    }

    [Theory]
    [InlineData("formatVersion", 0, "cache format differs")]
    [InlineData("toolVersion", "0.0.0-other", "tool version differs")]
    [InlineData("definitionsHash", "00", "search parameter definitions differ")]
    [InlineData("moduleVersions", "Ignixa.Search=00000000-0000-0000-0000-000000000000", "indexing code differs")]
    [InlineData("fhirVersion", "R5", "FHIR version differs")]
    public void GivenManifestFromAnotherFormatToolDefinitionsOrVersion_WhenSessionOpens_ThenWholeCacheIsRebuilt(string field, object value, string expectedReason)
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        first.GetIndex("Encounter");
        SimulateCacheWrittenByAnotherBuild(first.CacheDirectory!, field, value);
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();
        second.GetIndex("Encounter").FromCache.ShouldBeFalse();

        IssueItem issue = second.CacheIssues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("index-cache-rebuilt");
        issue.Diagnostics.ShouldContain(expectedReason);
    }

    [Fact]
    public void GivenCorruptTypeFile_WhenSessionOpens_ThenOnlyThatTypeIsRebuiltWithAWarning()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        first.GetIndex("Encounter");
        File.WriteAllText(Path.Combine(first.CacheDirectory!, "Patient.index.json"), "{ not json");
        FolderSession second = Open(folder, cache);

        ResourceTypeIndex patient = second.GetIndex("Patient");
        ResourceTypeIndex encounter = second.GetIndex("Encounter");

        patient.FromCache.ShouldBeFalse();
        patient.Resources.ShouldHaveSingleItem();
        encounter.FromCache.ShouldBeTrue();
        IssueItem issue = second.CacheIssues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("index-cache-corrupt");
        issue.Severity.ShouldBe("warning");
        issue.Diagnostics.ShouldContain("Patient");
        Open(folder, cache).GetIndex("Patient").FromCache.ShouldBeTrue();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    public void GivenTypeFileOfTheWrongShape_WhenSessionOpens_ThenItIsRebuiltWithAWarning(string content)
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        File.WriteAllText(Path.Combine(first.CacheDirectory!, "Patient.index.json"), content);
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();

        second.CacheIssues.ShouldHaveSingleItem().Code.ShouldBe("index-cache-corrupt");
    }

    [Fact]
    public void GivenTypeFileNamingAnUnknownSearchParameter_WhenSessionOpens_ThenItIsRebuiltWithAWarning()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        string path = Path.Combine(first.CacheDirectory!, "Patient.index.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"code\":\"family\"", "\"code\":\"no-such-parameter\"", StringComparison.Ordinal));
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();

        IssueItem issue = second.CacheIssues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("index-cache-corrupt");
        issue.Diagnostics.ShouldContain("no-such-parameter");
    }

    [Fact]
    public void GivenTypeFileWithAnInvalidValue_WhenSessionOpens_ThenItIsRebuiltWithAWarning()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        string path = Path.Combine(first.CacheDirectory!, "Patient.index.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"k\":\"string\"", "\"k\":\"mystery\"", StringComparison.Ordinal));
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();

        second.CacheIssues.ShouldHaveSingleItem().Code.ShouldBe("index-cache-corrupt");
    }

    [Fact]
    public void GivenCorruptManifest_WhenSessionOpens_ThenItIsReplacedWithAWarning()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        string manifestPath = Path.Combine(first.CacheDirectory!, "manifest.json");
        string original = File.ReadAllText(manifestPath);
        File.WriteAllText(manifestPath, "garbage");
        FolderSession second = Open(folder, cache);

        // The replacement is the manifest the folder state and tool produce, i.e. the one the type files were written under.
        second.GetIndex("Patient").Resources.ShouldHaveSingleItem();

        second.CacheIssues.ShouldHaveSingleItem().Code.ShouldBe("index-cache-corrupt");
        File.ReadAllText(manifestPath).ShouldBe(original);
        FolderSession third = Open(folder, cache);
        third.GetIndex("Patient").FromCache.ShouldBeTrue();
        third.CacheIssues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenCacheDisabled_WhenIndexed_ThenNothingIsWrittenAnywhere()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, IndexCacheOptions.Disabled);

        foreach (string type in FixtureTypes)
        {
            session.GetIndex(type).FromCache.ShouldBeFalse();
        }

        session.CacheDirectory.ShouldBeNull();
        session.CacheIssues.ShouldBeEmpty();
        Directory.EnumerateFileSystemEntries(cache.Path).ShouldBeEmpty();
    }

    [Fact]
    public void GivenNoCacheFlag_WhenOptionsAreResolved_ThenCacheIsDisabled()
    {
        var (command, cacheDir, noCache) = CreateCacheCommand();

        IndexCacheOptions options = CommonOptions.ResolveCache(command.Parse(["--no-cache"]), cacheDir, noCache);

        options.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void GivenCacheDirFlag_WhenOptionsAreResolved_ThenItOverridesTheUserDefault()
    {
        var (command, cacheDir, noCache) = CreateCacheCommand();
        using var cache = new TestFolder();

        IndexCacheOptions options = CommonOptions.ResolveCache(command.Parse(["--cache-dir", cache.Path]), cacheDir, noCache);

        options.RootDirectory.ShouldBe(cache.Path);
    }

    [Fact]
    public void GivenNeitherFlag_WhenOptionsAreResolved_ThenTheUserLevelDefaultIsUsed()
    {
        var (command, cacheDir, noCache) = CreateCacheCommand();

        IndexCacheOptions options = CommonOptions.ResolveCache(command.Parse([]), cacheDir, noCache);

        options.RootDirectory.ShouldNotBeNull().ShouldEndWith("cli-on-fhir");
    }

    [Fact]
    public void GivenCacheDirTogetherWithNoCache_WhenParsed_ThenItIsAUsageError()
    {
        var (command, _, _) = CreateCacheCommand();

        command.Parse(["--no-cache", "--cache-dir", "/somewhere"]).Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenCacheRootThatCannotBeCreated_WhenIndexed_ThenIndexIsStillServedWithOneUnavailableWarning()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        cache.Write("blocker", "a file where the cache root directory would have to be");
        var session = new FolderSession(
            folder.Path,
            new R4CoreSchemaProvider(),
            NullLoggerFactory.Instance,
            IndexCacheOptions.At(Path.Combine(cache.Path, "blocker")));

        ResourceTypeIndex patient = session.GetIndex("Patient");
        ResourceTypeIndex encounter = session.GetIndex("Encounter");

        patient.Resources.ShouldHaveSingleItem();
        encounter.Resources.ShouldNotBeEmpty();
        patient.FromCache.ShouldBeFalse();
        IssueItem issue = session.CacheIssues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("index-cache-unavailable");
        issue.Severity.ShouldBe("warning");
    }

    [Fact]
    public void GivenDefaultLocationCannotBeDetermined_WhenIndexed_ThenIndexIsStillServedWithAnUnavailableWarning()
    {
        using TestFolder folder = CreateFixture();
        var session = new FolderSession(
            folder.Path,
            new R4CoreSchemaProvider(),
            NullLoggerFactory.Instance,
            new IndexCacheOptions(null, "no home directory"));

        session.GetIndex("Patient").Resources.ShouldHaveSingleItem();

        IssueItem issue = session.CacheIssues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("index-cache-unavailable");
        issue.Diagnostics.ShouldContain("no home directory");
    }

    [Fact]
    public void GivenCaching_WhenIndexed_ThenTheSourceFolderIsNeverWrittenAndTheCacheLivesElsewhere()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        string[] before = Snapshot(folder.Path);
        FolderSession session = Open(folder, cache);

        foreach (string type in FixtureTypes)
        {
            session.GetIndex(type);
        }

        Snapshot(folder.Path).ShouldBe(before);
        session.CacheDirectory.ShouldNotBeNull().ShouldStartWith(cache.Path);
        session.CacheDirectory.ShouldEndWith("-R4");
        Directory.EnumerateFiles(session.CacheDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .ShouldBe(["Encounter.index.json", "Observation.index.json", "Patient.index.json", "manifest.json"]);
    }

    [Fact]
    public void GivenTwoFolders_WhenCached_ThenEachGetsItsOwnDirectoryUnderTheRoot()
    {
        using TestFolder one = CreateFixture();
        using TestFolder two = CreateFixture();
        using var cache = new TestFolder();

        FolderSession firstSession = Open(one, cache);
        FolderSession secondSession = Open(two, cache);
        firstSession.GetIndex("Patient");
        secondSession.GetIndex("Patient");
        string? first = firstSession.CacheDirectory;
        string? second = secondSession.CacheDirectory;

        first.ShouldNotBe(second);
        Directory.GetParent(first!)!.FullName.ShouldBe(cache.Path);
    }

    [Fact]
    public void GivenSeveralProcessesBuildingTheSameCache_WhenTheyFinish_ThenTheCacheIsCompleteAndLoadable()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();

        Parallel.For(0, 6, _ =>
        {
            FolderSession session = Open(folder, cache);
            foreach (string type in FixtureTypes)
            {
                session.GetIndex(type);
            }

            session.CacheIssues.ShouldBeEmpty();
        });

        FolderSession reader = Open(folder, cache);
        foreach (string type in FixtureTypes)
        {
            reader.GetIndex(type).FromCache.ShouldBeTrue();
        }

        reader.CacheIssues.ShouldBeEmpty();
        Directory.EnumerateFiles(reader.CacheDirectory!, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public void GivenCachedIndexes_WhenInspectedWithBuildIndex_ThenEachTypeReportsItsSourceAndTimes()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();

        ToolEnvelope<FolderInspection> cold = FolderInspector.Inspect(Open(folder, cache), buildIndexes: true);
        ToolEnvelope<FolderInspection> warm = FolderInspector.Inspect(Open(folder, cache), buildIndexes: true);

        cold.Facts.Index.ShouldNotBeNull().ResourceTypes.ShouldAllBe(type => type.Source == "built" && type.CacheWriteMilliseconds != null);
        IndexFacts warmFacts = warm.Facts.Index.ShouldNotBeNull();
        warmFacts.ResourceTypes.ShouldAllBe(type => type.Source == "cache" && type.CacheWriteMilliseconds == null);
        warmFacts.CacheDirectory.ShouldNotBeNull().ShouldStartWith(cache.Path);
        warm.Freshness.StaleFiles.ShouldBe(0);
        warm.Issues.Where(issue => issue.Code.StartsWith("index-cache", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void GivenStaleCache_WhenInspectedWithBuildIndex_ThenTheRebuildIsAnIssueInTheEnvelope()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderInspector.Inspect(Open(folder, cache), buildIndexes: true);
        folder.Write("patient.json", """{"resourceType":"Patient","id":"p1"}""");

        ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(Open(folder, cache), buildIndexes: true);

        envelope.Issues.ShouldContain(issue => issue.Code == "index-cache-rebuilt" && issue.Diagnostics.Contains("1 of the folder's files changed", StringComparison.Ordinal));
        envelope.Facts.Index.ShouldNotBeNull().ResourceTypes.ShouldAllBe(type => type.Source == "built");
        envelope.Freshness.StaleFiles.ShouldBe(0);
    }

    [Fact]
    public void GivenCacheDirectoryInsideTheDataFolder_WhenIndexed_ThenCacheIsRefusedAndNothingIsWrittenToTheFolder()
    {
        using TestFolder folder = CreateFixture();
        string[] before = Snapshot(folder.Path);

        foreach (IndexCacheOptions options in new[]
        {
            IndexCacheOptions.At(Path.Combine(folder.Path, "cache")),
            IndexCacheOptions.At(folder.Path),
            IndexCacheOptions.At(folder.Path + Path.DirectorySeparatorChar),
            IndexCacheOptions.UserDefault(Path.Combine(folder.Path, ".cache")),
        })
        {
            var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, options);

            session.GetIndex("Patient").FromCache.ShouldBeFalse();
            session.GetIndex("Encounter").Resources.ShouldNotBeEmpty();

            session.CacheDirectory.ShouldBeNull();
            IssueItem issue = session.CacheIssues.ShouldHaveSingleItem();
            issue.Code.ShouldBe("index-cache-unavailable");
            issue.Diagnostics.ShouldContain("inside the data folder");
        }

        Snapshot(folder.Path).ShouldBe(before);
    }

    [Fact]
    public void GivenDataFolderInsideTheCacheRoot_WhenIndexed_ThenCacheIsRefused()
    {
        using var cache = new TestFolder();
        string dataPath = Path.Combine(cache.Path, "data");
        Directory.CreateDirectory(dataPath);
        File.WriteAllText(Path.Combine(dataPath, "p.json"), """{"resourceType":"Patient","id":"p1"}""");
        var session = new FolderSession(dataPath, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, IndexCacheOptions.At(cache.Path));

        session.GetIndex("Patient").Resources.ShouldHaveSingleItem();

        session.CacheDirectory.ShouldBeNull();
        session.CacheIssues.ShouldHaveSingleItem().Diagnostics.ShouldContain("inside the cache directory");
        Directory.EnumerateFileSystemEntries(cache.Path).Select(Path.GetFileName).ShouldBe(["data"]);
    }

    [Fact]
    public void GivenSiblingDirectoryWithTheSamePrefix_WhenIndexed_ThenItIsNotMistakenForInsideTheFolder()
    {
        using TestFolder folder = CreateFixture();
        string sibling = folder.Path + "-cache";
        try
        {
            var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, IndexCacheOptions.At(sibling));

            session.GetIndex("Patient");

            session.CacheIssues.ShouldBeEmpty();
            session.CacheDirectory.ShouldNotBeNull().ShouldStartWith(sibling);
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public void GivenPersistedIndex_WhenTheTypeFileIsSniffed_ThenItIsNeverAFhirResource()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession session = Open(folder, cache);
        session.GetIndex("Patient");

        foreach (string file in Directory.EnumerateFiles(session.CacheDirectory!, "*.json"))
        {
            ResourceSniffer.Sniff(File.ReadAllBytes(file)).ResourceType.ShouldBeNull(file);
        }
    }

    [Theory]
    [InlineData("files-null")]
    [InlineData("files-duplicate-path")]
    [InlineData("files-null-element")]
    public void GivenManifestWithNullOrDuplicateMembers_WhenSessionOpens_ThenItIsReplacedWithAWarningInsteadOfFailing(string damage)
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        string manifestPath = Path.Combine(first.CacheDirectory!, "manifest.json");
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        switch (damage)
        {
            case "files-null": manifest["files"] = null; break;
            case "files-duplicate-path": manifest["files"]!.AsArray().Add(manifest["files"]![0]!.DeepClone()); break;
            case "files-null-element": manifest["files"]!.AsArray().Add(null); break;
            default: throw new ArgumentOutOfRangeException(nameof(damage), damage, null);
        }

        File.WriteAllText(manifestPath, manifest.ToJsonString());
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").Resources.ShouldHaveSingleItem();

        second.CacheIssues.ShouldHaveSingleItem().Code.ShouldBe("index-cache-corrupt");
        Open(folder, cache).CacheIssues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("resources-null")]
    [InlineData("resources-null-element")]
    [InlineData("resource-without-id")]
    [InlineData("entries-null")]
    [InlineData("entries-null-element")]
    [InlineData("diagnostics-null")]
    [InlineData("diagnostics-null-element")]
    [InlineData("indexed-type-missing")]
    [InlineData("duplicate-resource")]
    public void GivenTypeFileWithNullOrDuplicateMembers_WhenSessionOpens_ThenTheTypeIsRebuiltWithAWarningInsteadOfFailing(string damage)
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Observation");
        string path = Path.Combine(first.CacheDirectory!, "Observation.index.json");
        JsonNode index = JsonNode.Parse(File.ReadAllText(path))!;
        JsonNode firstResource = index["resources"]![0]!;
        switch (damage)
        {
            case "resources-null": index["resources"] = null; break;
            case "resources-null-element": index["resources"]!.AsArray().Add(null); break;
            case "resource-without-id": firstResource.AsObject().Remove("id"); break;
            case "entries-null": firstResource["entries"] = null; break;
            case "entries-null-element": firstResource["entries"]!.AsArray().Add(null); break;
            case "diagnostics-null": index["diagnostics"] = null; break;
            case "diagnostics-null-element": index["diagnostics"]!.AsArray().Add(null); break;
            case "indexed-type-missing": index.AsObject().Remove("indexedType"); break;
            default: index["resources"]!.AsArray().Add(firstResource.DeepClone()); break;
        }

        File.WriteAllText(path, index.ToJsonString());
        FolderSession second = Open(folder, cache);

        ResourceTypeIndex observation = second.GetIndex("Observation");
        ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(second, buildIndexes: true);

        observation.FromCache.ShouldBeFalse();
        observation.Resources.Count.ShouldBe(2);
        envelope.Issues.Count(issue => issue.Code == "index-cache-corrupt").ShouldBe(1);
        Open(folder, cache).GetIndex("Observation").FromCache.ShouldBeTrue();
    }

    [Fact]
    public void GivenValueTheCacheCannotReproduce_WhenIndexed_ThenTheTypeIsNotPersistedAndTheValueIsNamed()
    {
        using TestFolder folder = CreateFixture();
        folder.Write("ancient.json", """{"resourceType":"Patient","id":"ancient","birthDate":"0001-01-01"}""");
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);

        ResourceTypeIndex patients = first.GetIndex("Patient");
        ResourceTypeIndex encounters = first.GetIndex("Encounter");

        patients.Resources.Count.ShouldBe(2);
        patients.CacheWriteMilliseconds.ShouldBeNull();
        IssueItem issue = first.CacheIssues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("index-cache-unsupported-value");
        issue.Severity.ShouldBe("warning");
        issue.Diagnostics.ShouldContain("Patient");
        issue.Diagnostics.ShouldContain("'birthdate'");
        issue.Diagnostics.ShouldContain("Patient/ancient");
        File.Exists(Path.Combine(first.CacheDirectory!, "Patient.index.json")).ShouldBeFalse();
        encounters.CacheWriteMilliseconds.ShouldNotBeNull();

        FolderSession second = Open(folder, cache);
        second.GetIndex("Patient").FromCache.ShouldBeFalse();
        second.GetIndex("Encounter").FromCache.ShouldBeTrue();
        second.CacheIssues.Select(i => i.Code).ShouldBe(["index-cache-unsupported-value"]);
    }

    [Fact]
    public void GivenStringsWithSearchEscapeCharacters_WhenReloaded_ThenTheStoredTextIsNotUnescapedTwice()
    {
        string[] lines =
        [
            """{"resourceType":"Patient","id":"a","name":[{"family":"back\\slash","given":["x\\y"]}]}""",
            """{"resourceType":"Patient","id":"b","name":[{"family":"co,mma","given":["pi|pe","dol$lar"]}]}""",
            """{"resourceType":"Patient","id":"c","name":[{"family":"mix\\,ed\\|","given":["\\\\"]}]}""",
            """{"resourceType":"Patient","id":"d","name":[{"family":"keeps\\\\,backslash","given":["pi\\\\|pe","dol\\\\$lar"]}]}""",
        ];
        using TestFolder folder = new TestFolder().Write("patients.ndjson", string.Join('\n', lines));
        using var cache = new TestFolder();
        ResourceTypeIndex fresh = Open(folder, cache).GetIndex("Patient");

        ResourceTypeIndex loaded = Open(folder, cache).GetIndex("Patient");

        loaded.FromCache.ShouldBeTrue();
        SearchIndexCanon.Describe(loaded).ShouldBe(SearchIndexCanon.Describe(fresh));
        string.Join('\n', SearchIndexCanon.Describe(fresh)).ShouldContain("back\\slash");
        string.Join('\n', SearchIndexCanon.Describe(fresh)).ShouldContain("keeps\\,backslash");
    }

    [Theory]
    [InlineData("formatVersion", 1)]
    [InlineData("toolVersion", "0.0.0-older")]
    public void GivenCacheOfAnOlderShape_WhenSessionOpens_ThenItIsADifferentCacheNotACorruptOne(string field, object value)
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        first.GetIndex("Encounter");
        SimulateCacheWrittenByAnotherBuild(first.CacheDirectory!, field, value, inOlderShape: true);
        string manifestPath = Path.Combine(first.CacheDirectory!, "manifest.json");
        JsonNode olderManifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        olderManifest.AsObject().Remove("moduleVersions");
        olderManifest["files"] = new JsonObject();
        File.WriteAllText(manifestPath, olderManifest.ToJsonString());
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();
        second.GetIndex("Encounter").FromCache.ShouldBeFalse();

        second.CacheIssues.Select(issue => issue.Code).ShouldBe(["index-cache-rebuilt"]);
        FolderSession third = Open(folder, cache);
        third.GetIndex("Patient").FromCache.ShouldBeTrue();
        third.CacheIssues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTypeFileOfAnOlderShapeUnderAnotherManifest_WhenSessionOpens_ThenItIsAPlainMissWithoutAWarning()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        string path = Path.Combine(first.CacheDirectory!, "Patient.index.json");
        JsonNode index = JsonNode.Parse(File.ReadAllText(path))!;
        index["manifestId"] = "0000";
        index["resources"] = new JsonObject();
        File.WriteAllText(path, index.ToJsonString());
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();

        second.CacheIssues.ShouldBeEmpty();
        Open(folder, cache).GetIndex("Patient").FromCache.ShouldBeTrue();
    }

    [Fact]
    public void GivenPersistedCache_WhenTheManifestIsRead_ThenItNamesEveryAssemblyThatBuildsIndexes()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession session = Open(folder, cache);
        session.GetIndex("Patient");

        JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(session.CacheDirectory!, "manifest.json")))!;

        string[] modules = manifest["moduleVersions"]!.GetValue<string>().Split(';');
        modules.Select(module => module.Split('=')[0]).ShouldBe(["Ignixa.FhirPath", "Ignixa.Search", "Ignixa.Specification", "cli-on-fhir"], ignoreOrder: true);
        modules.Select(module => Guid.Parse(module.Split('=')[1])).ShouldAllBe(id => id != Guid.Empty);
    }

    [Fact]
    public void GivenCacheDirectoryThatIsASymlinkIntoTheDataFolder_WhenIndexed_ThenCacheIsRefusedAndNothingIsWritten()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // creating symbolic links needs a privilege Windows CI does not grant
        }

        using TestFolder folder = CreateFixture();
        using var elsewhere = new TestFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "sub"));
        string link = Path.Combine(elsewhere.Path, "cachelink");
        Directory.CreateSymbolicLink(link, Path.Combine(folder.Path, "sub"));
        string[] before = Snapshot(folder.Path);

        foreach (string cacheRoot in new[] { link, Path.Combine(link, "nested", "deeper") })
        {
            var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, IndexCacheOptions.At(cacheRoot));

            session.GetIndex("Patient").Resources.ShouldHaveSingleItem();

            session.CacheDirectory.ShouldBeNull();
            session.CacheIssues.ShouldHaveSingleItem().Diagnostics.ShouldContain("inside the data folder");
        }

        Snapshot(folder.Path).ShouldBe(before);
    }

    [Fact]
    public void GivenDataFolderReachedThroughASymlink_WhenCacheIsPlacedInsideItsRealPath_ThenCacheIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestFolder folder = CreateFixture();
        using var elsewhere = new TestFolder();
        string link = Path.Combine(elsewhere.Path, "datalink");
        Directory.CreateSymbolicLink(link, folder.Path);
        var session = new FolderSession(link, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, IndexCacheOptions.At(Path.Combine(folder.Path, "cache")));

        session.GetIndex("Patient").Resources.ShouldHaveSingleItem();

        session.CacheDirectory.ShouldBeNull();
        session.CacheIssues.ShouldHaveSingleItem().Code.ShouldBe("index-cache-unavailable");
        Directory.Exists(Path.Combine(folder.Path, "cache")).ShouldBeFalse();
    }

    [Fact]
    public void GivenPerFolderCacheDirectoryReplacedByASymlinkIntoTheDataFolder_WhenIndexed_ThenNothingIsWrittenThroughIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        string perFolderDirectory = DirectoryOf(Open(folder, cache));
        Directory.Delete(perFolderDirectory, recursive: true);
        string target = Path.Combine(folder.Path, "sub");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(perFolderDirectory, target);
        string[] before = Snapshot(folder.Path);
        FolderSession session = Open(folder, cache);

        session.GetIndex("Patient").Resources.ShouldHaveSingleItem();

        session.CacheIssues.ShouldHaveSingleItem().Diagnostics.ShouldContain("inside the data folder");
        Snapshot(folder.Path).ShouldBe(before);
    }

    [Fact]
    public void GivenDifferentlyCasedPaths_WhenCacheIsInsideOrAroundTheFolder_ThenCacheIsRefusedBecauseVolumesMayIgnoreCase()
    {
        using TestFolder folder = CreateFixture();
        string upper = folder.Path.ToUpperInvariant();

        foreach (string cacheRoot in new[] { Path.Combine(upper, "CACHE"), upper, Path.GetDirectoryName(upper)! })
        {
            var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, IndexCacheOptions.At(cacheRoot));

            session.GetIndex("Patient");

            session.CacheDirectory.ShouldBeNull(cacheRoot);
            session.CacheIssues.ShouldHaveSingleItem().Code.ShouldBe("index-cache-unavailable");
        }
    }

    [Theory]
    [InlineData("formatVersion", "cache format is missing")]
    [InlineData("toolVersion", "tool version is missing or not a string")]
    [InlineData("definitionsHash", "search parameter definitions is missing or not a string")]
    [InlineData("moduleVersions", "indexing code is missing or not a string")]
    public void GivenManifestMissingAnIdentityMember_WhenSessionOpens_ThenItIsADifferentCacheAndNeverSkipped(string member, string expectedReason)
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        RewriteManifest(first.CacheDirectory!, manifest => manifest.Remove(member));
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();

        IssueItem issue = second.CacheIssues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("index-cache-rebuilt");
        issue.Diagnostics.ShouldContain(expectedReason);
    }

    [Fact]
    public void GivenManifestIdentityOfTheWrongType_WhenSessionOpens_ThenItIsADifferentCache()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        RewriteManifest(first.CacheDirectory!, manifest => manifest["toolVersion"] = 7);
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();

        second.CacheIssues.ShouldHaveSingleItem().Diagnostics.ShouldContain("tool version is missing or not a string");
    }

    [Fact]
    public void GivenManifestWithPascalCaseMembers_WhenSessionOpens_ThenItDoesNotPassAsTheCurrentCache()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        RewriteManifest(first.CacheDirectory!, manifest =>
        {
            foreach (var (name, value) in manifest.ToArray())
            {
                manifest.Remove(name);
                manifest[char.ToUpperInvariant(name[0]) + name[1..]] = value;
            }
        });
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();

        second.CacheIssues.ShouldHaveSingleItem().Code.ShouldBe("index-cache-rebuilt");
    }

    [Fact]
    public void GivenStaleMemberAlsoPresentInAnotherCasing_WhenSessionOpens_ThenTheExactCasedMemberDecides()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        string current = string.Empty;
        RewriteManifest(first.CacheDirectory!, manifest =>
        {
            current = manifest["toolVersion"]!.GetValue<string>();
            manifest["toolVersion"] = "0.0.0-stale";
            manifest["ToolVersion"] = current;
        });

        FolderSession stale = Open(folder, cache);
        stale.GetIndex("Patient").FromCache.ShouldBeFalse();
        stale.CacheIssues.ShouldHaveSingleItem().Diagnostics.ShouldContain("tool version differs");

        RewriteManifest(first.CacheDirectory!, manifest =>
        {
            manifest["ToolVersion"] = "0.0.0-stale";
            manifest["toolVersion"] = current;
        });
        FolderSession inert = Open(folder, cache);
        inert.GetIndex("Patient").FromCache.ShouldBeTrue();
        inert.CacheIssues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenManifestRepeatingAMemberName_WhenSessionOpens_ThenItIsCorruptAndReplaced()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        string manifestPath = Path.Combine(first.CacheDirectory!, "manifest.json");
        string text = File.ReadAllText(manifestPath);
        File.WriteAllText(manifestPath, text.Replace("{\"formatVersion\"", "{\"toolVersion\":\"0.0.0-stale\",\"formatVersion\"", StringComparison.Ordinal));
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").Resources.ShouldHaveSingleItem();

        second.CacheIssues.ShouldHaveSingleItem().Code.ShouldBe("index-cache-corrupt");
        File.ReadAllText(manifestPath).ShouldBe(text);
    }

    [Fact]
    public void GivenTypeFileRepeatingAMemberName_WhenSessionOpens_ThenItIsCorruptAndRebuilt()
    {
        using TestFolder folder = CreateFixture();
        using var cache = new TestFolder();
        FolderSession first = Open(folder, cache);
        first.GetIndex("Patient");
        string path = Path.Combine(first.CacheDirectory!, "Patient.index.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"catalogEntryCount\":", "\"catalogEntryCount\":0,\"catalogEntryCount\":", StringComparison.Ordinal));
        FolderSession second = Open(folder, cache);

        second.GetIndex("Patient").FromCache.ShouldBeFalse();

        second.CacheIssues.ShouldHaveSingleItem().Code.ShouldBe("index-cache-corrupt");
    }

    [Fact]
    public void GivenRelativeLinkWithParentSegmentThroughAnotherLink_WhenCacheIsPlacedAtIt_ThenItIsResolvedLikeTheOperatingSystemAndRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // out/hop -> data/inner ; out/cache -> "hop/../c". The OS resolves hop first, so `..` is data and the
        // cache lands in data/c; resolving `..` lexically would put it in out/c.
        using var root = new TestFolder();
        root.Write("data/patient.json", """{"resourceType":"Patient","id":"p1"}""");
        Directory.CreateDirectory(Path.Combine(root.Path, "data", "inner"));
        Directory.CreateDirectory(Path.Combine(root.Path, "data", "c"));
        Directory.CreateDirectory(Path.Combine(root.Path, "out"));
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "out", "hop"), Path.Combine(root.Path, "data", "inner"));
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "out", "cache"), "hop/../c");
        string[] before = Snapshot(Path.Combine(root.Path, "data"));
        var session = new FolderSession(
            Path.Combine(root.Path, "data"),
            new R4CoreSchemaProvider(),
            NullLoggerFactory.Instance,
            IndexCacheOptions.At(Path.Combine(root.Path, "out", "cache")));

        session.GetIndex("Patient").Resources.ShouldHaveSingleItem();

        session.CacheDirectory.ShouldBeNull();
        session.CacheIssues.ShouldHaveSingleItem().Diagnostics.ShouldContain("inside the data folder");
        Snapshot(Path.Combine(root.Path, "data")).ShouldBe(before);
        Directory.Exists(Path.Combine(root.Path, "out", "c")).ShouldBeFalse();
    }

    [Fact]
    public void GivenRelativeLinkWithoutParentSegments_WhenCacheIsPlacedAtIt_ThenItIsResolvedFromTheLinksDirectoryAndRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TestFolder();
        root.Write("data/patient.json", """{"resourceType":"Patient","id":"p1"}""");
        Directory.CreateDirectory(Path.Combine(root.Path, "data", "sub"));
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "link"), "data/sub");
        var session = new FolderSession(
            Path.Combine(root.Path, "data"),
            new R4CoreSchemaProvider(),
            NullLoggerFactory.Instance,
            IndexCacheOptions.At(Path.Combine(root.Path, "link")));

        session.GetIndex("Patient").Resources.ShouldHaveSingleItem();

        session.CacheDirectory.ShouldBeNull();
        session.CacheIssues.ShouldHaveSingleItem().Diagnostics.ShouldContain("inside the data folder");
        Directory.EnumerateFileSystemEntries(Path.Combine(root.Path, "data", "sub")).ShouldBeEmpty();
    }

    [Fact]
    public void GivenRelativeLinksThatStayOutsideTheFolder_WhenCacheIsPlacedAtThem_ThenTheCacheIsUsed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TestFolder();
        root.Write("data/patient.json", """{"resourceType":"Patient","id":"p1"}""");
        Directory.CreateDirectory(Path.Combine(root.Path, "elsewhere", "real"));
        Directory.CreateDirectory(Path.Combine(root.Path, "out"));
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "out", "cache"), "../elsewhere/real");
        var session = new FolderSession(
            Path.Combine(root.Path, "data"),
            new R4CoreSchemaProvider(),
            NullLoggerFactory.Instance,
            IndexCacheOptions.At(Path.Combine(root.Path, "out", "cache")));

        session.GetIndex("Patient");

        session.CacheIssues.ShouldBeEmpty();
        Directory.EnumerateFiles(Path.Combine(root.Path, "elsewhere", "real"), "manifest.json", SearchOption.AllDirectories).ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenDataFolderPathThatCannotBeResolved_WhenTheCacheIsUsed_ThenItFailsClosedWithAnUnavailableWarning()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestFolder folder = CreateFixture();
        using var elsewhere = new TestFolder();
        Directory.CreateSymbolicLink(Path.Combine(elsewhere.Path, "loopA"), "loopB");
        Directory.CreateSymbolicLink(Path.Combine(elsewhere.Path, "loopB"), "loopA");
        FolderSession session = new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
        FolderCatalog unresolvable = session.Catalog with { RootPath = Path.Combine(elsewhere.Path, "loopA", "data") };
        string cacheRoot = Path.Combine(elsewhere.Path, "cache");
        var cache = new FolderIndexCache(IndexCacheOptions.At(cacheRoot), unresolvable, "R4", session.Definitions);

        cache.TryLoad("Patient").ShouldBeNull();

        cache.CacheDirectory.ShouldBeNull();
        IssueItem issue = cache.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("index-cache-unavailable");
        issue.Diagnostics.ShouldContain("cannot be resolved");
        Directory.Exists(cacheRoot).ShouldBeFalse();
    }

    private static (Command Command, Option<string?> CacheDir, Option<bool> NoCache) CreateCacheCommand()
    {
        Option<string?> cacheDir = CommonOptions.CreateCacheDirOption();
        Option<bool> noCache = CommonOptions.CreateNoCacheOption();
        var command = new Command("test");
        CommonOptions.AddCacheOptions(command, cacheDir, noCache);
        return (command, cacheDir, noCache);
    }

    // Rewrites the manifest as an older or foreign build would have written it and rebinds the type files to it, so
    // the cache is internally consistent but no longer matches this run.
    private static void SimulateCacheWrittenByAnotherBuild(string cacheDirectory, string field, object value, bool inOlderShape = false) =>
        RewriteManifest(cacheDirectory, manifest => manifest[field] = JsonValue.Create(value), inOlderShape);

    // Rewrites the manifest and re-binds the type files to the rewritten bytes, so the cache stays internally
    // consistent and only differs from what this run would write.
    private static void RewriteManifest(string cacheDirectory, Action<JsonObject> change, bool typeFilesInOlderShape = false)
    {
        string manifestPath = Path.Combine(cacheDirectory, "manifest.json");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        change(manifest);
        byte[] manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString());
        File.WriteAllBytes(manifestPath, manifestBytes);
        string manifestId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(manifestBytes));
        foreach (string typeFile in Directory.EnumerateFiles(cacheDirectory, "*.index.json"))
        {
            JsonNode index = JsonNode.Parse(File.ReadAllText(typeFile))!;
            index["manifestId"] = manifestId;
            if (typeFilesInOlderShape)
            {
                index["resources"] = new JsonObject();
            }

            File.WriteAllText(typeFile, index.ToJsonString());
        }
    }

    private static string DirectoryOf(FolderSession session)
    {
        session.GetIndex("Patient");
        return session.CacheDirectory!;
    }

    private static string[] Snapshot(string root) =>
    [
        .. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => File.Exists(path)
                ? $"{path} {new FileInfo(path).Length} {File.GetLastWriteTimeUtc(path).Ticks}"
                : path),
    ];

    private static FolderSession Open(TestFolder folder, TestFolder cache) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, IndexCacheOptions.At(cache.Path));

    private static TestFolder CreateFixture() => new TestFolder()
        .Write("patient.json", """
            {"resourceType":"Patient","id":"p1","name":[{"family":"Smith","given":["Ann","Marie"]}],"birthDate":"1980-01-02","gender":"female",
             "identifier":[{"type":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/v2-0203","code":"MR"}]},"system":"http://hospital.example/mrn","value":"12345"}]}
            """)
        .Write("encounters.ndjson", """
            {"resourceType":"Encounter","id":"e1","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"subject":{"reference":"Patient/p1"},"reasonCode":[{"text":"Annual Check-up Visit"}],"period":{"start":"2020-03-04T05:06:07-03:30","end":"2020-03-04T06:00:00-03:30"}}
            {"resourceType":"Encounter","id":"e2","status":"in-progress","class":{"code":"IMP"},"subject":{"reference":"http://other.example/fhir/Patient/p9"},"period":{"start":"2021"}}
            """)
        .Write("observations.ndjson", """
            {"resourceType":"Observation","id":"o1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8302-2","display":"Body height"}],"text":"Body Height"},"subject":{"reference":"Patient/p1"},"encounter":{"reference":"Encounter/e1"},"effectiveDateTime":"2020-03-04T05:06:07-03:30","valueQuantity":{"value":77.50,"unit":"cm","system":"http://unitsofmeasure.org","code":"cm"}}
            {"resourceType":"Observation","id":"o2","status":"final","code":{"text":"Panel"},"effectiveDateTime":"garbage","component":[{"code":{"coding":[{"system":"http://loinc.org","code":"8480-6"}]},"valueQuantity":{"value":120,"system":"http://unitsofmeasure.org","code":"mm[Hg]"}}]}
            """);
}
