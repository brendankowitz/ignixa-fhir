// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Application.Tests.Search.Expressions.Parsers;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Parsing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

public class ParameterOutcomeTests
{
    [Fact]
    public void GivenAPendingParameter_WhenBuiltWithoutPartialIndices_ThenItIsIgnoredWithThePendingReindexWarning()
    {
        var context = new SearchParserTestContext();
        var pending = context.Add("Patient", "pending", SearchParamType.String);
        pending.IsSearchable = false;
        var definitions = new SearchableSearchParameterDefinitionManager(context.DefinitionManager);
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => definitions, context.ValueParser, context.SchemaProvider),
            definitions);

        var options = builder.Build("Patient", [new QueryParameter("pending", "Smith")]);

        options.UnsupportedParams.ShouldBe(["pending"]);
        options.BundleIssues.ShouldHaveSingleItem().Diagnostics
            .ShouldBe("Search parameter 'pending' is pending reindex and was ignored.");
    }

    [Fact]
    public void GivenAPendingParameter_WhenBuiltWithPartialIndices_ThenItCompilesAndWarnsThatResultsMayBeIncomplete()
    {
        var context = new SearchParserTestContext();
        var pending = context.Add("Patient", "pending", SearchParamType.String);
        pending.IsSearchable = false;
        var definitions = new SearchableSearchParameterDefinitionManager(context.DefinitionManager, () => true);
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => definitions, context.ValueParser, context.SchemaProvider),
            definitions);

        var options = builder.Build("Patient", [new QueryParameter("pending", "Smith")]);

        options.Expression.ShouldNotBeNull();
        options.BundleIssues.ShouldHaveSingleItem().Diagnostics
            .ShouldBe("Search results may be incomplete because search parameter 'pending' is pending reindex.");
    }

    [Fact]
    public void GivenAPendingSortParameter_WhenBuiltWithPartialIndices_ThenItSortsAndWarnsThatResultsMayBeIncomplete()
    {
        var context = new SearchParserTestContext();
        var pending = context.Add("Patient", "pending", SearchParamType.String);
        pending.IsSearchable = false;
        var definitions = new SearchableSearchParameterDefinitionManager(context.DefinitionManager, () => true);
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => definitions, context.ValueParser, context.SchemaProvider),
            definitions);

        var options = builder.Build("Patient", [new QueryParameter("_sort", "pending")]);

        options.Sort.ShouldHaveSingleItem().Parameter.Code.ShouldBe("pending");
        options.BundleIssues.ShouldHaveSingleItem().Diagnostics
            .ShouldBe("Search results may be incomplete because search parameter 'pending' is pending reindex.");
    }

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
