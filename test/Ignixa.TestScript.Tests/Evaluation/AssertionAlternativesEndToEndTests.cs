using Ignixa.Abstractions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Reporting;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Evaluation;

public class AssertionAlternativesEndToEndTests
{
    private readonly ITestRequestProvider _mockProvider;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _schema;

    public AssertionAlternativesEndToEndTests()
    {
        _mockProvider = Substitute.For<ITestRequestProvider>();
        _fixtureProvider = new InlineFixtureProvider();
        _schema = Substitute.For<IFhirSchemaProvider>();
    }

    [Fact]
    public async Task GivenSubscriptionDeleteReadbackWorkedExample_WhenExecutingEndToEnd_ThenBothGroupsPassViaMatchedAlternative()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"SubscriptionDeleteReadback","status":"active",
              "test":[{"name":"delete then readback","action":[
                {"operation":{"type":{"code":"delete"},"url":"Subscription/sub-1","responseId":"delete-response"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"delete-status"}],
                  "responseCode":"200","warningOnly":true,"description":"Completed synchronously"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"delete-status"}],
                  "responseCode":"202","warningOnly":true,"description":"Accepted asynchronously"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"delete-status"}],
                  "responseCode":"204","warningOnly":true,"description":"Completed with no content"}},
                {"operation":{"type":{"code":"read"},"url":"Subscription/sub-1"}},
                {"assert":{
                  "extension":[
                    {"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"readback"},
                    {"url":"http://ignixa.io/testscript/assertionWhenResponseStatus","extension":[
                      {"url":"sourceId","valueString":"delete-response"},
                      {"url":"status","valueInteger":202}
                    ]}
                  ],
                  "responseCode":"200","warningOnly":true,
                  "description":"An asynchronous delete may still be readable immediately"
                }},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"readback"}],
                  "response":"notFound","warningOnly":true,"description":"404 when tracked as gone"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"readback"}],
                  "response":"gone","warningOnly":true,"description":"410 when tracked as deleted"}}
              ]}]
            }
            """;

        var parseResult = TestScriptParser.Parse(json);
        parseResult.IsSuccess.ShouldBeTrue();

        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new TestResponse
            {
                StatusCode = call.Arg<TestRequest>().Method == HttpMethod.Delete ? 202 : 200
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(parseResult.Value!, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        var actions = report.TestResults[0].Actions;
        actions.Count.ShouldBe(4);

        var deleteStatusGroup = actions[1];
        deleteStatusGroup.GroupId.ShouldBe("delete-status");
        deleteStatusGroup.Outcome.ShouldBe(TestScriptOutcome.Pass);
        deleteStatusGroup.Members!.Single(m => m.Passed).Description.ShouldBe("Accepted asynchronously");

        var readbackGroup = actions[3];
        readbackGroup.GroupId.ShouldBe("readback");
        readbackGroup.Outcome.ShouldBe(TestScriptOutcome.Pass);
        readbackGroup.Members!.Single(m => m.Passed).Description
            .ShouldBe("An asynchronous delete may still be readable immediately");
    }
}
