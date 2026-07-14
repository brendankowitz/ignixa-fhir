using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.ConformanceMatrix.Cli.Commands;
using Ignixa.ConformanceMatrix.Cli.Reporting;

namespace Ignixa.ConformanceMatrix.Cli.Tests;

public class RunCommandTests
{
    private static ImplReportResult MakeResult(string status) =>
        new()
        {
            Id = "test",
            File = "test.json",
            Status = status,
            DurationMs = 0
        };

    [Fact]
    public void GivenMixedResults_WhenFormattingSummary_ThenIncludesSkippedAndErrorCounts()
    {
        // Arrange
        var results = new List<ImplReportResult>
        {
            MakeResult("pass"),
            MakeResult("fail"),
            MakeResult("skipped"),
            MakeResult("error"),
            MakeResult("skipped")
        };

        // Act
        var summary = RunCommand.FormatOutcomeSummary(results);

        // Assert
        summary.ShouldBe("1 passed, 1 failed, 2 skipped, 1 error(s)");
    }

    [Fact]
    public void GivenBareTokenValue_WhenNormalizingAuthHeader_ThenUsesAuthorizationHeader()
    {
        var (name, value) = RunCommand.ParseAuthHeader("Bearer abc123");

        name.ShouldBe("Authorization");
        value.ShouldBe("Bearer abc123");
    }

    [Fact]
    public void GivenExplicitHeaderValue_WhenNormalizingAuthHeader_ThenPreservesHeaderName()
    {
        var (name, value) = RunCommand.ParseAuthHeader("X-Test: value");

        name.ShouldBe("X-Test");
        value.ShouldBe("value");
    }

    private static JsonObject MakeTestReport(string display) =>
        new()
        {
            ["resourceType"] = "TestReport",
            ["testScript"] = new JsonObject { ["display"] = display }
        };

    [Fact]
    public void GivenCustomAuthScheme_WhenNormalizingAuthHeader_ThenTreatsItAsBareCredential()
    {
        // Arrange: a scheme not on any hardcoded list, whose credential contains a colon.
        var (name, value) = RunCommand.ParseAuthHeader("AWS4-HMAC-SHA256 Credential=abc/20260714:xyz");

        name.ShouldBe("Authorization");
        value.ShouldBe("AWS4-HMAC-SHA256 Credential=abc/20260714:xyz");
    }

    [Fact]
    public void GivenNoAuthHeader_WhenApplying_ThenSucceedsWithoutSettingAHeader()
    {
        // Arrange
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, null);

        // Assert
        error.ShouldBeNull();
        httpClient.DefaultRequestHeaders.Authorization.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Authorization:")]
    [InlineData("X-Api-Key:   ")]
    public void GivenAuthHeaderWithNoValue_WhenApplying_ThenReportsErrorRatherThanRunningUnauthenticated(string authHeader)
    {
        // Arrange: an env var expanding to empty is the common case; applying nothing would run the
        // whole suite unauthenticated and report every 401 as a legitimate failure.
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, authHeader);

        // Assert
        error.ShouldNotBeNull();
        error.ShouldContain("resolves to no header value");
        httpClient.DefaultRequestHeaders.Authorization.ShouldBeNull();
    }

    [Fact]
    public void GivenInvalidHeaderName_WhenApplying_ThenReportsErrorRatherThanDroppingTheCredential()
    {
        // Arrange: '@' is not a valid HTTP token, so TryAddWithoutValidation returns false.
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, "Api@Key: abc123");

        // Assert
        error.ShouldNotBeNull();
        error.ShouldContain("not a valid HTTP header name");
    }

    [Fact]
    public void GivenBearerToken_WhenApplying_ThenSetsAuthorizationHeaderOnTheClient()
    {
        // Arrange
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, "Bearer abc123");

        // Assert
        error.ShouldBeNull();
        httpClient.DefaultRequestHeaders.Authorization!.Scheme.ShouldBe("Bearer");
        httpClient.DefaultRequestHeaders.Authorization.Parameter.ShouldBe("abc123");
    }

    [Fact]
    public void GivenCustomHeaderName_WhenApplying_ThenAddsItToTheClient()
    {
        // Arrange
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, "X-Api-Key: abc123");

        // Assert
        error.ShouldBeNull();
        httpClient.DefaultRequestHeaders.GetValues("X-Api-Key").ShouldBe(["abc123"]);
    }

    [Fact]
    public void GivenUnparseableAuthorizationValue_WhenApplying_ThenFallsBackToAddingItVerbatim()
    {
        // Arrange: a credential Authorization cannot parse must still reach the wire.
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, "Authorization: AWS4-HMAC-SHA256 Credential=abc/20260714:xyz");

        // Assert
        error.ShouldBeNull();
        httpClient.DefaultRequestHeaders.GetValues("Authorization")
            .ShouldBe(["AWS4-HMAC-SHA256 Credential=abc/20260714:xyz"]);
    }

    [Fact]
    public void GivenJsonFormat_WhenBuildingPayload_ThenProducesTheNativeImplReportShape()
    {
        // Arrange: this is the shape 'merge' deserializes; a regression breaks the matrix pipeline.
        var results = new List<ImplReportResult> { MakeResult("pass") };

        // Act
        var payload = RunCommand.BuildPayload(ReportFormat.Json, "my-server", DateTimeOffset.UnixEpoch, 42, results, []);

        // Assert
        var parsed = JsonNode.Parse(payload)!;
        parsed["impl"]!.GetValue<string>().ShouldBe("my-server");
        parsed["duration_ms"]!.GetValue<long>().ShouldBe(42);
        parsed["results"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenFhirFormat_WhenBuildingPayload_ThenProducesATestReportBundle()
    {
        // Act
        var payload = RunCommand.BuildPayload(
            ReportFormat.Fhir, "my-server", DateTimeOffset.UnixEpoch, 42, [], [MakeTestReport("Search/basic.json")]);

        // Assert
        var parsed = JsonNode.Parse(payload)!;
        parsed["resourceType"]!.GetValue<string>().ShouldBe("Bundle");
        parsed["entry"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenNoReports_WhenBuildingPayload_ThenOmitsEntryBecauseFhirForbidsEmptyArrays()
    {
        // Arrange: reachable when every script fails to parse.

        // Act
        var payload = RunCommand.BuildTestReportPayload([], DateTimeOffset.UnixEpoch);

        // Assert
        payload["resourceType"]!.GetValue<string>().ShouldBe("Bundle");
        payload.ContainsKey("entry").ShouldBeFalse();
    }

    [Fact]
    public void GivenSingleReport_WhenBuildingPayload_ThenStillReturnsBundleCollection()
    {
        var payload = RunCommand.BuildTestReportPayload([MakeTestReport("Search/basic.json")], DateTimeOffset.UnixEpoch);

        payload.ShouldNotBeNull();
        payload["resourceType"]!.GetValue<string>().ShouldBe("Bundle");
        payload["type"]!.GetValue<string>().ShouldBe("collection");
        payload["entry"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenMultipleReports_WhenBuildingPayload_ThenEntriesCarryUniqueAbsoluteFullUrls()
    {
        // Arrange/Act: fullUrl must be absolute per R4, and unique within the Bundle per bdl-7.
        var payload = RunCommand.BuildTestReportPayload(
            [MakeTestReport("Search/intervals.json"), MakeTestReport("CRUD/basic.json")],
            DateTimeOffset.UnixEpoch);

        // Assert
        var entries = payload["entry"]!.AsArray();
        entries.Count.ShouldBe(2);

        var fullUrls = entries.Select(e => e!["fullUrl"]!.GetValue<string>()).ToList();
        fullUrls.ShouldAllBe(url => Uri.IsWellFormedUriString(url, UriKind.Absolute));
        fullUrls.Distinct().Count().ShouldBe(2);
        entries[0]!["resource"]!["resourceType"]!.GetValue<string>().ShouldBe("TestReport");
    }

    [Fact]
    public void GivenReports_WhenBuildingPayload_ThenBundleCarriesRunTimestamp()
    {
        var startedAt = new DateTimeOffset(2026, 7, 14, 9, 30, 0, TimeSpan.Zero);

        var payload = RunCommand.BuildTestReportPayload([MakeTestReport("Search/basic.json")], startedAt);

        payload["timestamp"]!.GetValue<string>().ShouldBe(startedAt.ToString("o"));
    }

    [Fact]
    public async Task GivenSuccessfulMetadataResponse_WhenFetching_ThenReturnsParsedCapabilityStatement()
    {
        using var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"resourceType":"CapabilityStatement","status":"active"}""",
                    Encoding.UTF8,
                    "application/fhir+json")
            });
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ResourceType.ShouldBe("CapabilityStatement");
    }

    [Fact]
    public async Task GivenNonSuccessMetadataResponse_WhenFetching_ThenReturnsNull()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenUnparseableMetadataBody_WhenFetching_ThenReturnsNull()
    {
        using var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json", Encoding.UTF8, "application/fhir+json")
            });
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenNetworkFailure_WhenFetching_ThenReturnsNull()
    {
        using var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenCancellationRequested_WhenFetching_ThenThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHandler(_ => throw new OperationCanceledException());
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        await Should.ThrowAsync<OperationCanceledException>(
            () => RunCommand.FetchCapabilityStatementAsync(httpClient, cts.Token));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
