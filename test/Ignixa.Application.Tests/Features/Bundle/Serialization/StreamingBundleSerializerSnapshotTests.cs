// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using NSubstitute;
using Shouldly;
using FhirBundleLink = Ignixa.Models.BundleLink;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Golden snapshot tests. The searchset and R4 history fixtures capture happy-path output from
/// the unmodified StreamingBundleSerializer, before the mid-stream error handling rewrite -- they
/// are the regression baseline proving the buffering rewrite leaves happy-path output byte-identical.
/// The Stu3 history fixture could not be captured early: Task 5 (design doc Section 4) deliberately
/// changed that output, suppressing the response element per Stu3 bdl-4, so it is captured post-change
/// by construction. All captured with pretty: false -- design doc Section 1 documents that
/// pretty-printed whitespace inside entries legitimately changes under buffering, so a pretty golden
/// would pin the wrong thing.
/// </summary>
public class StreamingBundleSerializerSnapshotTests
{
    private const string BaseUrl = "http://localhost:5000/Patient";
    private const string QueryString = "?_count=10";
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GivenASearchsetBundleWithNoWarningIssues_WhenSerializing_ThenOutputMatchesGoldenSnapshot()
    {
        // Arrange
        var entries = new List<SearchEntryResult>
        {
            CreateMatchEntry("Patient", "golden-patient-1"),
            CreateMatchEntry("Patient", "golden-patient-2"),
            CreateIncludeEntry("Organization", "golden-org-1"),
        };
        var searchOptions = new SearchOptions { MaxItemCount = 10 };
        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream,
            "searchset",
            total: 2,
            CreateAsyncEnumerable(entries),
            searchOptions,
            BaseUrl,
            QueryString,
            pretty: false);

        var json = Encoding.UTF8.GetString(outputStream.ToArray());

        // Assert
        AssertMatchesSnapshot(json, "searchset-happy-path.json");
    }

    [Fact]
    public async Task GivenAnR4HistoryBundle_WhenSerializing_ThenOutputMatchesGoldenSnapshot()
    {
        // Arrange
        var entries = new List<SearchEntryResult>
        {
            CreateHistoryEntry("Patient", "golden-patient-1", versionId: "1", isDeleted: false),
            CreateHistoryEntry("Patient", "golden-patient-1", versionId: "2", isDeleted: true),
        };
        var links = new List<FhirBundleLink> { CreateLink("self", "http://x/Patient/_history") };
        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeHistoryAsync(
            outputStream,
            "history",
            total: 2,
            CreateAsyncEnumerable(entries),
            links,
            pretty: false,
            pageSize: 20);

        var json = Encoding.UTF8.GetString(outputStream.ToArray());

        // Assert
        AssertMatchesSnapshot(json, "r4-history-happy-path.json");
    }

    [Fact]
    public async Task GivenAStu3HistoryBundle_WhenSerializing_ThenOutputMatchesGoldenSnapshotAndCarriesNoResponseElement()
    {
        // Arrange
        var entries = new List<SearchEntryResult>
        {
            CreateHistoryEntry("Patient", "golden-patient-1", versionId: "1", isDeleted: false),
            CreateHistoryEntry("Patient", "golden-patient-1", versionId: "2", isDeleted: true),
        };
        var links = new List<FhirBundleLink> { CreateLink("self", "http://x/Patient/_history") };
        var outputStream = new MemoryStream();
        var schemaProvider = Substitute.For<ISchema>();
        schemaProvider.Version.Returns(FhirVersion.Stu3);

        // Act
        await StreamingBundleSerializer.SerializeHistoryAsync(
            outputStream,
            "history",
            total: 2,
            CreateAsyncEnumerable(entries),
            links,
            schemaProvider: schemaProvider,
            pretty: false,
            pageSize: 20);

        var json = Encoding.UTF8.GetString(outputStream.ToArray());

        // Assert
        AssertMatchesSnapshot(json, "stu3-history.json");
        var entryElements = JsonDocument.Parse(json).RootElement.GetProperty("entry").EnumerateArray().ToList();
        entryElements.Count(e => e.TryGetProperty("response", out _)).ShouldBe(0);
    }

    private static void AssertMatchesSnapshot(string actualJson, string fileName)
    {
        string path = GetSnapshotPath(fileName);
        string expectedJson = File.ReadAllText(path);
        actualJson.ShouldBe(expectedJson);
    }

    /// <summary>
    /// Resolves against the test assembly's output directory, where the csproj copies the snapshots, rather
    /// than against <c>[CallerFilePath]</c>.
    /// <para>
    /// A caller file path is baked in at compile time, and CI builds with
    /// <c>ContinuousIntegrationBuild=true</c>, which rewrites source paths to a deterministic <c>/_/</c>
    /// root. So the old form resolved to <c>/_/test/…/Snapshots/…</c> and threw
    /// <see cref="DirectoryNotFoundException"/> on every CI run, while passing locally for the only reason
    /// it ever passed: the machine that compiled it was the machine that ran it.
    /// </para>
    /// </summary>
    private static string GetSnapshotPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Features", "Bundle", "Serialization", "Snapshots", fileName);

    private static async IAsyncEnumerable<SearchEntryResult> CreateAsyncEnumerable(List<SearchEntryResult> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            await Task.Yield();
        }
    }

    private static SearchEntryResult CreateMatchEntry(string resourceType, string id)
    {
        var resourceJson = $$"""
        {"resourceType":"{{resourceType}}","id":"{{id}}","name":[{"family":"Golden"}]}
        """;

        return new SearchEntryResult(
            ResourceType: resourceType,
            ResourceId: id,
            VersionId: "1",
            LastModified: FixedTimestamp,
            ResourceBytes: Encoding.UTF8.GetBytes(resourceJson))
        {
            SearchMode = SearchEntryMode.Match
        };
    }

    private static SearchEntryResult CreateIncludeEntry(string resourceType, string id)
    {
        var resourceJson = $$"""
        {"resourceType":"{{resourceType}}","id":"{{id}}","name":"Golden Organization"}
        """;

        return new SearchEntryResult(
            ResourceType: resourceType,
            ResourceId: id,
            VersionId: "1",
            LastModified: FixedTimestamp,
            ResourceBytes: Encoding.UTF8.GetBytes(resourceJson))
        {
            SearchMode = SearchEntryMode.Include
        };
    }

    private static SearchEntryResult CreateHistoryEntry(string resourceType, string id, string versionId, bool isDeleted)
    {
        var resourceJson = $$"""
        {"resourceType":"{{resourceType}}","id":"{{id}}","meta":{"versionId":"{{versionId}}"},"name":[{"family":"Golden"}]}
        """;

        return new SearchEntryResult(
            ResourceType: resourceType,
            ResourceId: id,
            VersionId: versionId,
            LastModified: FixedTimestamp,
            ResourceBytes: Encoding.UTF8.GetBytes(resourceJson))
        {
            SearchMode = SearchEntryMode.Match,
            IsDeleted = isDeleted
        };
    }

    private static FhirBundleLink CreateLink(string relation, string url)
    {
        var link = new FhirBundleLink { Url = url };
        link.SetProperty("relation", JsonValue.Create(relation));
        return link;
    }
}
