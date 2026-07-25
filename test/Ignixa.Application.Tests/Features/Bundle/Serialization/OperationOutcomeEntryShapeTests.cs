// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Search.Models;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Shape tests for StreamingBundleSerializer.WriteOperationOutcomeEntry, per design doc §3.
/// Verifies element presence AND absence, since absence is load-bearing for FHIR conformance
/// (e.g. a stray `resource` on the R5 history shape violates bdl-3b).
/// </summary>
public class OperationOutcomeEntryShapeTests
{
    private const string FullUrl = "urn:uuid:00000000-0000-0000-0000-0000000000e0";
    private const string SelfUrl = "http://localhost:5000/Patient/_history";

    private static readonly IssueComponent Issue = new("fatal", "exception", Diagnostics: "Bundle serialization failed: boom");

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public void GivenSearchsetBundleType_WhenWritingEntry_ThenResourceCarriesOutcomeWithSearchModeOutcomeAndNoRequestOrResponse(FhirVersion version)
    {
        // Arrange & Act
        var entry = WriteSingleEntry("searchset", version);

        // Assert
        entry.GetProperty("fullUrl").GetString().ShouldBe(FullUrl);

        var resource = entry.GetProperty("resource");
        resource.GetProperty("resourceType").GetString().ShouldBe("OperationOutcome");
        var issue = resource.GetProperty("issue")[0];
        issue.GetProperty("severity").GetString().ShouldBe("fatal");
        issue.GetProperty("code").GetString().ShouldBe("exception");
        issue.GetProperty("diagnostics").GetString().ShouldBe(Issue.Diagnostics);

        entry.GetProperty("search").GetProperty("mode").GetString().ShouldBe("outcome");

        entry.TryGetProperty("request", out _).ShouldBeFalse();
        entry.TryGetProperty("response", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public void GivenHistoryBundleTypeAtR4OrLater_WhenWritingEntry_ThenOutcomeIsCarriedInResponseWithNoResourceOrSearch(FhirVersion version)
    {
        // Arrange & Act
        var entry = WriteSingleEntry("history", version);

        // Assert
        entry.GetProperty("fullUrl").GetString().ShouldBe(FullUrl);

        var request = entry.GetProperty("request");
        request.GetProperty("method").GetString().ShouldBe("GET");
        request.GetProperty("url").GetString().ShouldBe(SelfUrl);

        var response = entry.GetProperty("response");
        response.GetProperty("status").GetString().ShouldBe("500");
        var outcome = response.GetProperty("outcome");
        outcome.GetProperty("resourceType").GetString().ShouldBe("OperationOutcome");
        var issue = outcome.GetProperty("issue")[0];
        issue.GetProperty("severity").GetString().ShouldBe("fatal");
        issue.GetProperty("code").GetString().ShouldBe("exception");
        issue.GetProperty("diagnostics").GetString().ShouldBe(Issue.Diagnostics);

        entry.TryGetProperty("resource", out _).ShouldBeFalse();
        entry.TryGetProperty("search", out _).ShouldBeFalse();
    }

    [Fact]
    public void GivenHistoryBundleTypeAtStu3_WhenWritingEntry_ThenOutcomeIsCarriedAsResourceWithRequestAndNoResponse()
    {
        // Arrange & Act
        var entry = WriteSingleEntry("history", FhirVersion.Stu3);

        // Assert
        entry.GetProperty("fullUrl").GetString().ShouldBe(FullUrl);

        var resource = entry.GetProperty("resource");
        resource.GetProperty("resourceType").GetString().ShouldBe("OperationOutcome");
        var issue = resource.GetProperty("issue")[0];
        issue.GetProperty("severity").GetString().ShouldBe("fatal");
        issue.GetProperty("code").GetString().ShouldBe("exception");
        issue.GetProperty("diagnostics").GetString().ShouldBe(Issue.Diagnostics);

        var request = entry.GetProperty("request");
        request.GetProperty("method").GetString().ShouldBe("GET");
        request.GetProperty("url").GetString().ShouldBe(SelfUrl);

        entry.TryGetProperty("response", out _).ShouldBeFalse();
        entry.TryGetProperty("search", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("batch-response")]
    [InlineData("transaction-response")]
    public void GivenBatchOrTransactionResponseBundleType_WhenWritingEntry_ThenItDelegatesToTheExistingErrorEntryShape(string bundleType)
    {
        // Arrange & Act
        var entry = WriteSingleEntry(bundleType, FhirVersion.R4);

        // Assert
        entry.GetProperty("response").GetProperty("status").GetString().ShouldBe("500 Internal Server Error");

        var resource = entry.GetProperty("resource");
        resource.GetProperty("resourceType").GetString().ShouldBe("OperationOutcome");
        var issue = resource.GetProperty("issue")[0];
        issue.GetProperty("severity").GetString().ShouldBe("fatal");
        issue.GetProperty("code").GetString().ShouldBe("exception");

        entry.TryGetProperty("fullUrl", out _).ShouldBeFalse();
        entry.TryGetProperty("search", out _).ShouldBeFalse();
    }

    [Fact]
    public void GivenAnUnrecognizedBundleType_WhenWritingEntry_ThenOnlyFullUrlAndResourceAreWritten()
    {
        // Arrange & Act
        var entry = WriteSingleEntry("collection", FhirVersion.R4);

        // Assert
        entry.GetProperty("fullUrl").GetString().ShouldBe(FullUrl);

        var resource = entry.GetProperty("resource");
        resource.GetProperty("resourceType").GetString().ShouldBe("OperationOutcome");
        var issue = resource.GetProperty("issue")[0];
        issue.GetProperty("severity").GetString().ShouldBe("fatal");
        issue.GetProperty("code").GetString().ShouldBe("exception");

        entry.TryGetProperty("request", out _).ShouldBeFalse();
        entry.TryGetProperty("response", out _).ShouldBeFalse();
        entry.TryGetProperty("search", out _).ShouldBeFalse();
    }

    private static JsonElement WriteSingleEntry(string bundleType, FhirVersion version)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = FhirJsonWriter.Create(buffer);

        writer.UnderlyingWriter.WriteStartArray();
        StreamingBundleSerializer.WriteOperationOutcomeEntry(writer, Issue, bundleType, version, FullUrl, SelfUrl);
        writer.UnderlyingWriter.WriteEndArray();
        writer.UnderlyingWriter.Flush();

        string json = Encoding.UTF8.GetString(buffer.WrittenSpan);
        return JsonDocument.Parse(json).RootElement[0];
    }
}
