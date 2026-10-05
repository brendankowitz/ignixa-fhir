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
/// Tests for the adjacent helper corrections in StreamingBundleSerializer:
/// empty-string tolerance in WriteBundleIssues/WriteBundleIssuesPreR5, empty-URL
/// tolerance in WriteBundleLinks, and the malformed warning-entry fullUrl.
/// </summary>
public class StreamingBundleSerializerHelperTests
{
    private const string BaseUrl = "http://localhost:5000/Patient";
    private const string QueryString = "?_count=10";

    [Fact]
    public async Task GivenAnIssueWithAnEmptyLocation_WhenSerializingPreR5_ThenTheEmptyValueIsSkipped()
    {
        // Arrange
        var issues = new[] { new IssueComponent("warning", "incomplete", Diagnostics: "d", Location: ["", "Patient.name"]) };

        // Act
        var json = await SerializeSearchsetWithIssuesAsync(issues, FhirVersion.R4);

        // Assert
        var locations = JsonDocument.Parse(json).RootElement
            .GetProperty("entry")[0].GetProperty("resource")
            .GetProperty("issue")[0].GetProperty("location");
        locations.GetArrayLength().ShouldBe(1);
        locations[0].GetString().ShouldBe("Patient.name");
    }

    [Fact]
    public async Task GivenAnIssueWithAnEmptyLocation_WhenSerializingR5_ThenTheEmptyValueIsSkipped()
    {
        // Arrange
        var issues = new[] { new IssueComponent("warning", "incomplete", Diagnostics: "d", Location: ["", "Patient.name"]) };

        // Act
        var json = await SerializeSearchsetWithIssuesAsync(issues, FhirVersion.R5);

        // Assert
        var locations = JsonDocument.Parse(json).RootElement
            .GetProperty("issues")
            .GetProperty("issue")[0].GetProperty("location");
        locations.GetArrayLength().ShouldBe(1);
        locations[0].GetString().ShouldBe("Patient.name");
    }

    [Fact]
    public async Task GivenAnIssueWithOnlyEmptyLocations_WhenSerializingPreR5_ThenTheLocationArrayIsOmitted()
    {
        // Arrange
        var issues = new[] { new IssueComponent("warning", "incomplete", Diagnostics: "d", Location: [" ", ""]) };

        // Act
        var json = await SerializeSearchsetWithIssuesAsync(issues, FhirVersion.R4);

        // Assert
        var issue = JsonDocument.Parse(json).RootElement
            .GetProperty("entry")[0].GetProperty("resource")
            .GetProperty("issue")[0];
        issue.TryGetProperty("location", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAnIssueWithAnEmptyExpression_WhenSerializingPreR5_ThenTheEmptyValueIsSkipped()
    {
        // Arrange
        var issues = new[] { new IssueComponent("warning", "incomplete", Diagnostics: "d", Expression: ["", "Patient.name"]) };

        // Act
        var json = await SerializeSearchsetWithIssuesAsync(issues, FhirVersion.R4);

        // Assert
        var expressions = JsonDocument.Parse(json).RootElement
            .GetProperty("entry")[0].GetProperty("resource")
            .GetProperty("issue")[0].GetProperty("expression");
        expressions.GetArrayLength().ShouldBe(1);
        expressions[0].GetString().ShouldBe("Patient.name");
    }

    [Fact]
    public async Task GivenAHistoryLinkWithAnEmptyUrl_WhenSerializing_ThenTheLinkIsOmitted()
    {
        // Arrange -- deliberately NOT a "next" link: SerializeHistoryAsync strips those
        // whenever !hasMore || entryCount == 0 (:350-355), which would filter the link
        // before WriteBundleLinks runs and make this test vacuous.
        var links = new[] { CreateLink("self", "http://x/_history"), CreateLink("prev", null) };

        // Act
        var json = await SerializeHistoryWithLinksAsync(links);

        // Assert
        var relations = JsonDocument.Parse(json).RootElement.GetProperty("link")
            .EnumerateArray().Select(l => l.GetProperty("relation").GetString()).ToList();
        relations.ShouldBe(["self"]);
    }

    [Fact]
    public async Task GivenASearchsetWithWarningIssues_WhenSerializing_ThenTheOutcomeEntryFullUrlIsAWellFormedUuidUrn()
    {
        // Arrange
        var issues = new[] { new IssueComponent("warning", "incomplete", Diagnostics: "d") };

        // Act
        var json = await SerializeSearchsetWithIssuesAsync(issues, FhirVersion.R4);

        // Assert
        var fullUrl = JsonDocument.Parse(json).RootElement.GetProperty("entry")[0].GetProperty("fullUrl").GetString();
        fullUrl.ShouldBe("urn:uuid:00000000-0000-0000-0000-0000000000d0");
        Guid.TryParse(fullUrl!["urn:uuid:".Length..], out _).ShouldBeTrue();
    }

    private static async Task<string> SerializeSearchsetWithIssuesAsync(IReadOnlyList<IssueComponent> issues, FhirVersion version)
    {
        var schemaProvider = Substitute.For<ISchema>();
        schemaProvider.Version.Returns(version);

        var searchOptions = new SearchOptions { MaxItemCount = 10, BundleIssues = issues };
        var outputStream = new MemoryStream();

        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream,
            "searchset",
            null,
            EmptyEntries(),
            searchOptions,
            BaseUrl,
            QueryString,
            schemaProvider);

        return Encoding.UTF8.GetString(outputStream.ToArray());
    }

    private static async Task<string> SerializeHistoryWithLinksAsync(IReadOnlyList<FhirBundleLink> links)
    {
        var outputStream = new MemoryStream();

        await StreamingBundleSerializer.SerializeHistoryAsync(
            outputStream,
            "history",
            null,
            EmptyEntries(),
            links,
            pretty: false,
            pageSize: 20);

        return Encoding.UTF8.GetString(outputStream.ToArray());
    }

    private static async IAsyncEnumerable<SearchEntryResult> EmptyEntries()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static FhirBundleLink CreateLink(string relation, string? url)
    {
        var link = new FhirBundleLink { Url = url };
        link.SetProperty("relation", JsonValue.Create(relation));
        return link;
    }
}
