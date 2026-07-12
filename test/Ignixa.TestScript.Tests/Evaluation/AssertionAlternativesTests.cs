using Ignixa.Abstractions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Evaluation;

public class AssertionAlternativesTests
{
    private readonly ITestRequestProvider _mockProvider;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _schema;

    public AssertionAlternativesTests()
    {
        _mockProvider = Substitute.For<ITestRequestProvider>();
        _fixtureProvider = new InlineFixtureProvider();
        _schema = Substitute.For<IFhirSchemaProvider>();
    }

    private static TestScriptDefinition SingleTestDefinition(string name, params ActionExpression[] actions) =>
        new()
        {
            Metadata = new TestScriptMetadata { Name = name },
            Tests = [new TestPhaseDefinition { Name = "t", Actions = actions }]
        };

    [Fact]
    public async Task GivenGroupWherePreferredMemberPasses_WhenExecuting_ThenAggregatePassesAndCarriesBothMembers()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 410 });

        var definition = SingleTestDefinition("GroupPreferred",
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/deleted-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("gone"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Preferred: 410 Gone"
            },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("notFound"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Alternative: 404 Not Found"
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Pass);
        report.TestResults[0].Actions.Count.ShouldBe(2);
        var groupAction = report.TestResults[0].Actions[1];
        groupAction.Outcome.ShouldBe(TestScriptOutcome.Pass);
        groupAction.GroupId.ShouldBe("deleted-resource-readback");
        groupAction.Members!.Count.ShouldBe(2);
        groupAction.Members[0].Passed.ShouldBeTrue();
        groupAction.Members[1].Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenGroupWhereOnlyFallbackMemberPasses_WhenExecuting_ThenAggregatePassesWithoutWarning()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 404 });

        var definition = SingleTestDefinition("GroupFallback",
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/deleted-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("gone"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Preferred: 410 Gone"
            },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("notFound"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Alternative: 404 Not Found"
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Pass);
        var groupAction = report.TestResults[0].Actions[1];
        groupAction.Outcome.ShouldBe(TestScriptOutcome.Pass);
        groupAction.Members![0].Passed.ShouldBeFalse();
        groupAction.Members[1].Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenGroupWhereNoMemberPasses_WhenExecuting_ThenAggregateFails()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 500 });

        var definition = SingleTestDefinition("GroupNoneMatch",
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/deleted-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("gone"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Preferred: 410 Gone"
            },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("notFound"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Alternative: 404 Not Found"
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Fail);
        report.TestResults[0].Actions[1].Outcome.ShouldBe(TestScriptOutcome.Fail);
    }

    [Fact]
    public async Task GivenGroupWhereNoMemberIsApplicable_WhenExecuting_ThenAggregateErrors()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = SingleTestDefinition("GroupNoneApplicable",
            new OperationExpression { Type = "delete", Resource = "Patient", Params = "/deleted-id", ResponseId = "delete-response" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("okay"),
                AnyOfGroupId = "conditional-group",
                WarningOnly = true,
                Description = "Only applies if delete returned 202",
                WhenResponseStatus = new ResponseStatusCondition("delete-response", [202])
            },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("gone"),
                AnyOfGroupId = "conditional-group",
                WarningOnly = true,
                Description = "Only applies if delete returned 204",
                WhenResponseStatus = new ResponseStatusCondition("delete-response", [204])
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Error);
        var groupAction = report.TestResults[0].Actions[1];
        groupAction.Outcome.ShouldBe(TestScriptOutcome.Error);
        groupAction.Message.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenGroupMemberWithUnresolvableSourceId_WhenExecuting_ThenAggregateErrorsNamingMember()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 404 });

        var definition = SingleTestDefinition("GroupBadSourceId",
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/deleted-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("gone"),
                AnyOfGroupId = "bad-group",
                WarningOnly = true,
                Description = "Broken conditional member",
                WhenResponseStatus = new ResponseStatusCondition("does-not-exist", [202])
            },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("notFound"),
                AnyOfGroupId = "bad-group",
                WarningOnly = true,
                Description = "Alternative: 404 Not Found"
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Error);
        var groupAction = report.TestResults[0].Actions[1];
        groupAction.Outcome.ShouldBe(TestScriptOutcome.Error);
        groupAction.Message!.ShouldContain("Broken conditional member");
    }

    [Fact]
    public async Task GivenStandaloneConditionalAssertionWhoseConditionMatches_WhenExecuting_ThenEvaluatedNormally()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new TestResponse
            {
                StatusCode = call.Arg<TestRequest>().Method == HttpMethod.Delete ? 202 : 200
            });

        var definition = SingleTestDefinition("StandaloneConditionMatches",
            new OperationExpression { Type = "delete", Resource = "Patient", Params = "/async-id", ResponseId = "delete-response" },
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/async-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("okay"),
                WarningOnly = true,
                Description = "An asynchronous delete may still be readable immediately",
                WhenResponseStatus = new ResponseStatusCondition("delete-response", [202])
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Pass);
        report.TestResults[0].Actions[2].Outcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenStandaloneConditionalAssertionWhoseConditionDoesNotMatch_WhenExecuting_ThenRecordedAsSkip()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new TestResponse
            {
                StatusCode = call.Arg<TestRequest>().Method == HttpMethod.Delete ? 204 : 404
            });

        var definition = SingleTestDefinition("StandaloneConditionMismatch",
            new OperationExpression { Type = "delete", Resource = "Patient", Params = "/async-id", ResponseId = "delete-response" },
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/async-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("okay"),
                WarningOnly = true,
                Description = "An asynchronous delete may still be readable immediately",
                WhenResponseStatus = new ResponseStatusCondition("delete-response", [202])
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Actions[2].Outcome.ShouldBe(TestScriptOutcome.Skip);
        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Pass);
    }
}
