using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle;

public class DefinitionsEventIdTests
{
    [Fact]
    public void GivenResourcesExtractedAtDifferentPositions_WhenAllocationPositionIsSelected_ThenMinimumIsUsed()
    {
        var resources = new[]
        {
            Patient("newer", definitionsEventId: 29),
            Patient("older", definitionsEventId: 11),
            Patient("newest", definitionsEventId: 41),
        };

        var result = DefinitionsEventIdSelector.GetMinimum(resources);

        result.ShouldBe(11);
    }

    private static ResourceWrapper Patient(string id, long definitionsEventId) =>
        new(
            "Patient",
            id,
            "1",
            DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
            new ResourceRequest("PUT", $"Patient/{id}"))
        {
            DefinitionsEventId = definitionsEventId,
        };
}
