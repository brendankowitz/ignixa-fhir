using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
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

        var mockProvider = Substitute.For<ITestRequestProvider>();
        mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "example"}""")
            });

        var schema = Substitute.For<IFhirSchemaProvider>();
        var evaluator = new TestScriptEvaluator(mockProvider, new InlineFixtureProvider(), schema);

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

        var mockProvider = Substitute.For<ITestRequestProvider>();
        mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "example"}""")
            });

        var schema = Substitute.For<IFhirSchemaProvider>();
        var evaluator = new TestScriptEvaluator(mockProvider, new InlineFixtureProvider(), schema);
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

        var mockProvider = Substitute.For<ITestRequestProvider>();
        mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new TestResponse { StatusCode = 201, Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "test-123"}""") },
                new TestResponse { StatusCode = 200, Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "test-123"}""") },
                new TestResponse { StatusCode = 204 }
            );

        var schema = Substitute.For<IFhirSchemaProvider>();
        var evaluator = new TestScriptEvaluator(mockProvider, new InlineFixtureProvider(), schema);

        var report = await evaluator.ExecuteAsync(parseResult.Value!, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        report.SetupResult.ShouldNotBeNull();
        report.TestResults.Count.ShouldBe(1);
        report.TeardownResult.ShouldNotBeNull();
    }
}
