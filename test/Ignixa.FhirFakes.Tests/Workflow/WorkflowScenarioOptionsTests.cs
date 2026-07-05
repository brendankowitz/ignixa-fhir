// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Workflow;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class WorkflowScenarioOptionsTests
{
    [Fact]
    public void GivenDefaultOptions_WhenCreated_ThenSeedIsNullAndClockIsSystem()
    {
        var options = new WorkflowScenarioOptions();

        options.Seed.ShouldBeNull();
        options.Clock.ShouldBe(TimeProvider.System);
        options.Tag.ShouldBeNull();
    }

    [Fact]
    public void GivenTwoOptionsWithSameValues_WhenComparing_ThenTheyAreEqual()
    {
        var first = new WorkflowScenarioOptions { Seed = 5, Tag = "test" };
        var second = new WorkflowScenarioOptions { Seed = 5, Tag = "test" };

        first.ShouldBe(second);
    }
}
