// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Parsing;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

public class ParameterTraceTests
{
    private static ParameterTrace Valid()
        => new(
            ordinal: 0,
            key: "name",
            keySyntax: null,
            value: "Smith",
            valueSyntax: null,
            ir: null,
            outcome: new ParameterOutcome.Compiled(),
            dataType: null);

    [Fact]
    public void GivenANegativeOrdinal_WhenConstructed_ThenItThrows()
    {
        // The ordinal feeds CteProvenance.ParameterOrdinal, so an unchecked value here surfaces stages
        // later naming a parameter this producer never touched.
        Should.Throw<ArgumentOutOfRangeException>(() => new ParameterTrace(
            ordinal: -1,
            key: "name",
            keySyntax: null,
            value: "Smith",
            valueSyntax: null,
            ir: null,
            outcome: new ParameterOutcome.Compiled(),
            dataType: null));
    }

    [Fact]
    public void GivenAnEmptyKey_WhenConstructed_ThenItThrows()
        => Should.Throw<ArgumentException>(() => new ParameterTrace(
            ordinal: 0,
            key: string.Empty,
            keySyntax: null,
            value: "Smith",
            valueSyntax: null,
            ir: null,
            outcome: new ParameterOutcome.Compiled(),
            dataType: null));

    [Fact]
    public void GivenAnEmptyValue_WhenConstructed_ThenItIsAccepted()
    {
        // Valueless shapes (_not-referenced, includes) legitimately carry no value.
        var trace = new ParameterTrace(
            ordinal: 0,
            key: "_not-referenced",
            keySyntax: null,
            value: string.Empty,
            valueSyntax: null,
            ir: null,
            outcome: new ParameterOutcome.Compiled(),
            dataType: null);

        trace.Value.ShouldBe(string.Empty);
    }

    [Fact]
    public void GivenAnOutcomeRestamp_WhenCopied_ThenOnlyTheOutcomeChanges()
    {
        // Outcome is the one init property: SearchCompiler restamps it when a later stage attributes a
        // failure back to this parameter. Everything else must survive the copy unchanged.
        var original = Valid();

        var restamped = original with { Outcome = new ParameterOutcome.Ignored("nope", null) };

        restamped.Outcome.ShouldBeOfType<ParameterOutcome.Ignored>();
        restamped.Ordinal.ShouldBe(original.Ordinal);
        restamped.Key.ShouldBe(original.Key);
        restamped.Value.ShouldBe(original.Value);
    }
}
