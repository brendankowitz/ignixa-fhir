using Ignixa.Domain.Models;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle;

public class DefinitionsEventIdTests
{
    [Fact]
    public void GivenResourcesExtractedAtDifferentPositions_WhenAllocationPositionIsSelected_ThenMinimumIsUsed()
    {
        ResourceWrapper[] resources = [TestWritePath.Patient("newer", 29), TestWritePath.Patient("older", 11), TestWritePath.Patient("newest", 41)];

        DefinitionsEventIdSelector.GetMinimum(resources).ShouldBe(11);
    }
}
