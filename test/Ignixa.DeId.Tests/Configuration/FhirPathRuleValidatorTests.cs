// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Configuration;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.DeId.Tests.Configuration;

/// <summary>
/// Validation compiles each rule path into the tier its evaluation reads, so validating a server-defined rule
/// neither spends a slot in the bounded request-supplied tier nor leaves the server tier cold.
/// </summary>
public class FhirPathRuleValidatorTests
{
    [Fact]
    public void GivenServerDefinedRule_WhenValidated_ThenItsPathIsCompiledIntoTheServerTier()
    {
        // Regression: every rule path was compiled into the request-supplied tier.
        const string path = "Patient.id | Patient.validatorServerTierMarker";
        var rule = new FhirPathRule { IsServerDefined = true, Path = path, Method = "redact" };

        FhirPathRuleValidator.EnsureValid([rule]);

        TypedElementExtensions.IsServerExpressionCached(path).ShouldBeTrue();
        TypedElementExtensions.IsRequestSuppliedExpressionCached(path).ShouldBeFalse();
    }

    [Fact]
    public void GivenRequestSuppliedRule_WhenValidated_ThenItsPathStaysOutOfTheServerTier()
    {
        // Guard: request-supplied paths must not reach the server tier, where they could evict the server's own.
        const string path = "Patient.id | Patient.validatorRequestTierMarker";
        var rule = new FhirPathRule { Path = path, Method = "redact" };

        FhirPathRuleValidator.EnsureValid([rule]);

        TypedElementExtensions.IsServerExpressionCached(path).ShouldBeFalse();
    }
}
