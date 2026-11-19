// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Darts.Configuration;

namespace Ignixa.DeId.Tests.Configuration;

/// <summary>
/// The built-in policies are what a caller gets when they supply no configuration, so neither may
/// continue after a processor failure (that would return the data in clear as a success).
/// </summary>
public class BootstrapPoliciesTests
{
    [Fact]
    public void GivenSafeHarborPolicy_WhenCreated_ThenProcessorFailuresStopTheOperation()
    {
        // Regression: SAFE_HARBOR used LogAndContinue.
        var options = BootstrapPolicies.CreateSafeHarborOptions();

        options.Processing!.ErrorHandling.ShouldBe(ErrorHandlingMode.StopOnError);
    }

    [Fact]
    public void GivenExpertDeterminationPolicy_WhenCreated_ThenProcessorFailuresFailFast()
    {
        // Guard: already FailFast at the base commit.
        var options = BootstrapPolicies.CreateExpertDeterminationOptions();

        options.Processing!.ErrorHandling.ShouldBe(ErrorHandlingMode.FailFast);
    }
}
