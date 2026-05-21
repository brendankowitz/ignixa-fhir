using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Reporting;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Integration;

public class EndToEndTests
{
    [Fact]
    public async Task GivenSimpleReadScript_WhenExecutedEndToEnd_ThenPasses()
    {
        var json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "simple-read.json"));

        var parseResult = TestScriptParser.Parse(json);
        parseResult.IsSuccess.ShouldBeTrue();

        var mockClient = Substitute.For<IFhirClient>();
        mockClient.BaseUrl.Returns("http://test-server");
        mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse
            {
                StatusCode = 200,
                Body = JsonNode.Parse("""{"resourceType": "Patient", "id": "example"}""")
            });

        var registry = new SingleClientRegistry(mockClient);
        var schema = Substitute.For<IFhirSchemaProvider>();
        var evaluator = new TestScriptEvaluator(registry, new InlineFixtureProvider(), schema);

        var report = await evaluator.ExecuteAsync(parseResult.Value!, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        report.TestScriptName.ShouldBe("SimpleReadTest");
    }

    [Fact]
    public async Task GivenReport_WhenGeneratingTestReport_ThenProducesValidFhirResource()
    {
        var json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "simple-read.json"));
        var parseResult = TestScriptParser.Parse(json);

        var mockClient = Substitute.For<IFhirClient>();
        mockClient.BaseUrl.Returns("http://test-server");
        mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse
            {
                StatusCode = 200,
                Body = JsonNode.Parse("""{"resourceType": "Patient", "id": "example"}""")
            });

        var registry = new SingleClientRegistry(mockClient);
        var schema = Substitute.For<IFhirSchemaProvider>();
        var evaluator = new TestScriptEvaluator(registry, new InlineFixtureProvider(), schema);
        var report = await evaluator.ExecuteAsync(parseResult.Value!, CancellationToken.None);

        var testReport = TestReportResourceGenerator.Generate(report);

        testReport["resourceType"]?.GetValue<string>().ShouldBe("TestReport");
        testReport["result"]?.GetValue<string>().ShouldBe("pass");
        testReport["name"]?.GetValue<string>().ShouldBe("SimpleReadTest");
    }

    [Fact]
    public async Task GivenCreateReadDeleteScript_WhenExecuted_ThenAllPhasesRun()
    {
        var json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "create-read-delete.json"));

        var parseResult = TestScriptParser.Parse(json);
        parseResult.IsSuccess.ShouldBeTrue();

        var mockClient = Substitute.For<IFhirClient>();
        mockClient.BaseUrl.Returns("http://test-server");
        mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new FhirResponse { StatusCode = 201, Body = JsonNode.Parse("""{"resourceType": "Patient", "id": "test-123"}""") },
                new FhirResponse { StatusCode = 200, Body = JsonNode.Parse("""{"resourceType": "Patient", "id": "test-123"}""") },
                new FhirResponse { StatusCode = 204 }
            );

        var registry = new SingleClientRegistry(mockClient);
        var schema = Substitute.For<IFhirSchemaProvider>();
        var evaluator = new TestScriptEvaluator(registry, new InlineFixtureProvider(), schema);

        var report = await evaluator.ExecuteAsync(parseResult.Value!, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        report.SetupResult.ShouldNotBeNull();
        report.TestResults.Count.ShouldBe(1);
        report.TeardownResult.ShouldNotBeNull();
    }
}
