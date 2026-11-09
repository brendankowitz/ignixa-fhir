using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Domain.Exceptions;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

/// <summary>Spec §6.1: the kickoff body accepts exactly the listed parameters; anything else is a 400 (F12).</summary>
public class ReindexRequestParserTests
{
    [Fact]
    public void GivenSupportedParameters_WhenParsing_ThenReturnsAManualCommand()
    {
        const string body = """
            {
              "resourceType": "Parameters",
              "parameter": [
                { "name": "maximumNumberOfResourcesPerQuery", "valueInteger": 50 },
                { "name": "maximumNumberOfResourcesPerWrite", "valueInteger": 25 },
                { "name": "maximumConcurrency", "valueInteger": 2 },
                { "name": "queryDelayIntervalInMilliseconds", "valueInteger": 5 }
              ]
            }
            """;

        ReindexRequestParser.Parse(body).ShouldBe(new CreateReindexJobCommand
        {
            MaximumNumberOfResourcesPerQuery = 50,
            MaximumNumberOfResourcesPerWrite = 25,
            MaximumConcurrency = 2,
            QueryDelayIntervalInMilliseconds = 5,
            Trigger = ReindexTriggerKind.Manual
        });
    }

    [Theory]
    [InlineData("""{"name":"unexpected","valueString":"value"}""", "Unknown reindex parameter 'unexpected'.")]
    [InlineData("""{"name":"targetResourceTypes","valueString":"Patient"}""", "Parameter 'targetResourceTypes' is not supported.")]
    [InlineData("""{"name":"maximumConcurrency","valueString":"2"}""", "maximumConcurrency must be an integer.")]
    [InlineData("""{"name":"queryDelayIntervalInMilliseconds","valueDecimal":1.5}""", "queryDelayIntervalInMilliseconds must be an integer.")]
    [InlineData("""{"valueInteger":5}""", "Each Parameters.parameter must have a name.")]
    [InlineData("""{"name":"maximumConcurrency","valueInteger":2},{"name":"maximumConcurrency","valueInteger":2}""", "Parameter 'maximumConcurrency' must not be repeated.")]
    public void GivenAnInvalidParameter_WhenParsing_ThenItIsABadRequest(string parameters, string expectedError)
    {
        var body = $$"""{"resourceType":"Parameters","parameter":[{{parameters}}]}""";

        Should.Throw<BadRequestException>(() => ReindexRequestParser.Parse(body)).Message.ShouldBe(expectedError);
    }

    [Theory]
    [InlineData("{not json", "Invalid request body. Expected a FHIR Parameters resource.")]
    [InlineData("null", "Expected a FHIR Parameters resource.")]
    [InlineData("[1]", "Expected a FHIR Parameters resource.")]
    [InlineData("""{"resourceType":"Patient"}""", "Expected a FHIR Parameters resource.")]
    [InlineData("""{"resourceType":"Parameters","parameter":{}}""", "Parameters.parameter must be an array.")]
    public void GivenABodyThatIsNotParameters_WhenParsing_ThenItIsABadRequest(string body, string expectedError)
    {
        Should.Throw<BadRequestException>(() => ReindexRequestParser.Parse(body)).Message.ShouldBe(expectedError);
    }
}
