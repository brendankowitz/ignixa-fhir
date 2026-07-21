// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Parsing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

public class ParameterOutcomeTests
{
    [Fact]
    public void GivenAnUnsupportedModifier_WhenBuilt_ThenTheParameterIsReportedAsIgnored()
    {
        var harness = SearchOptionsBuilderHarness.ForPatient(("birthdate", SearchParamType.Date));
        var outcomes = new List<ParameterTrace>();

        harness.Build([("birthdate:exact", "2000-01-01")], outcomes);

        var trace = outcomes.ShouldHaveSingleItem();
        trace.Key.ShouldBe("birthdate:exact");
        trace.Outcome.ShouldBeOfType<ParameterOutcome.Ignored>();
    }

    [Fact]
    public void GivenAValidParameter_WhenBuilt_ThenItIsReportedAsCompiled()
    {
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));
        var outcomes = new List<ParameterTrace>();

        harness.Build([("name", "Smith")], outcomes);

        outcomes.ShouldHaveSingleItem().Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
    }

    [Fact]
    public void GivenAChainedKey_WhenBuilt_ThenKeySyntaxRetainsTheChainStructure()
    {
        var harness = SearchOptionsBuilderHarness.ForPatientChainedThrough(
            "general-practitioner", "Practitioner", "name", SearchParamType.String);
        var outcomes = new List<ParameterTrace>();

        harness.Build([("general-practitioner.name", "Smith")], outcomes);

        var trace = outcomes.ShouldHaveSingleItem();
        trace.Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        trace.KeySyntax.ShouldNotBeNull();
        trace.KeySyntax!.Kind.ShouldBe("ForwardChain");
        trace.KeySyntax.Children.ShouldHaveSingleItem();
    }
}
