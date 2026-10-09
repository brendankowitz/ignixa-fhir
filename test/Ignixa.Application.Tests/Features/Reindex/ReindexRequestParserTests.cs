using Ignixa.Application.Features.Reindex;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Reindex;

public class ReindexRequestParserTests
{
    [Fact]
    public void GivenSupportedParameters_WhenParsing_ThenReturnsReindexRequest()
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

        var parsed = ReindexRequestParser.TryParse(body, out var request, out var error);

        parsed.ShouldBeTrue(error);
        error.ShouldBeNull();
        request.ShouldNotBeNull();
        request.MaximumNumberOfResourcesPerQuery.ShouldBe(50);
        request.MaximumNumberOfResourcesPerWrite.ShouldBe(25);
        request.MaximumConcurrency.ShouldBe(2);
        request.QueryDelayIntervalInMilliseconds.ShouldBe(5);
    }

    [Theory]
    [InlineData("unexpected", "Unknown reindex parameter 'unexpected'.")]
    [InlineData("targetResourceTypes", "Parameter 'targetResourceTypes' is not supported.")]
    [InlineData("targetSearchParameterTypes", "Parameter 'targetSearchParameterTypes' is not supported.")]
    public void GivenUnsupportedParameter_WhenParsing_ThenReturnsError(string name, string expectedError)
    {
        var body = $$"""{"resourceType":"Parameters","parameter":[{"name":"{{name}}","valueString":"value"}]}""";

        var parsed = ReindexRequestParser.TryParse(body, out var request, out var error);

        parsed.ShouldBeFalse();
        request.ShouldBeNull();
        error.ShouldBe(expectedError);
    }
}
