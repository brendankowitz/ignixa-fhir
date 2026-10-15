// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Caching;
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
    private static void SimulateCacheWrittenByAnotherBuild(string cacheDirectory, string field, object value)
    {
        string manifestPath = Path.Combine(cacheDirectory, "manifest.json");
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest[field] = JsonValue.Create(value);
        byte[] manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString());
        File.WriteAllBytes(manifestPath, manifestBytes);
        string manifestId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(manifestBytes));
        foreach (string typeFile in Directory.EnumerateFiles(cacheDirectory, "*.index.json"))
        {
            JsonNode index = JsonNode.Parse(File.ReadAllText(typeFile))!;
            index["manifestId"] = manifestId;
            File.WriteAllText(typeFile, index.ToJsonString());
        }
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
