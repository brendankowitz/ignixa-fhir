// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Application.Tests.Search.Expressions.Parsers;
using Ignixa.Search.Definition;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Specification.ValueSets.Normative;
using NSubstitute;
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

    [Theory]
    [InlineData("_include", "*", false)]
    [InlineData("_include", "Patient:*", false)]
    [InlineData("_revinclude", "*", false)]
    [InlineData("_revinclude", "Observation:*", false)]
    [InlineData("_include", "*", true)]
    [InlineData("_include", "Patient:*", true)]
    [InlineData("_revinclude", "*", true)]
    [InlineData("_revinclude", "Observation:*", true)]
    public void GivenAWildcardIncludeWithAPendingReference_WhenBuilt_ThenItWarnsOfIncompleteResultsAndTracksTheParameter(
        string parameterName,
        string parameterValue,
        bool usePartialIndices)
    {
        var context = new SearchParserTestContext();
        var pending = context.Add(
            parameterName == "_include" ? "Patient" : "Observation",
            "pending-reference",
            SearchParamType.Reference,
            ["Organization", "Patient"]);
        pending.IsSearchable = false;
        var definitions = new SearchableSearchParameterDefinitionManager(
            context.DefinitionManager,
            () => usePartialIndices);
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => definitions, context.ValueParser, context.SchemaProvider),
            definitions);

        var options = builder.Build("Patient", [new QueryParameter(parameterName, parameterValue)]);

        options.ResolvedSearchParameters.ShouldContain(parameter => ReferenceEquals(parameter, pending));
        options.BundleIssues.ShouldContain(issue => issue.Diagnostics ==
            "Search results may be incomplete because search parameter 'pending-reference' is pending reindex.");
    }

    [Theory]
    [InlineData("_include", "Patient:*", "Patient", "Resource", false)]
    [InlineData("_include", "Patient:*", "Patient", "Resource", true)]
    [InlineData("_include", "Patient:*", "Patient", "DomainResource", false)]
    [InlineData("_include", "Patient:*", "Patient", "DomainResource", true)]
    [InlineData("_revinclude", "Observation:*", "Observation", "Resource", false)]
    [InlineData("_revinclude", "Observation:*", "Observation", "Resource", true)]
    [InlineData("_revinclude", "Observation:*", "Observation", "DomainResource", false)]
    [InlineData("_revinclude", "Observation:*", "Observation", "DomainResource", true)]
    public void GivenAWildcardIncludeWithAnAbstractPendingReference_WhenBuilt_ThenItWarnsAndTracksTheExpandedParameter(
        string parameterName,
        string parameterValue,
        string sourceResourceType,
        string baseResourceType,
        bool usePartialIndices)
    {
        var context = new SearchParserTestContext();
        var pending = new SearchParameterInfo(
            name: "pending-abstract-reference",
            code: "pending-abstract-reference",
            searchParamType: SearchParamType.Reference,
            targetResourceTypes: ["Patient"],
            baseResourceTypes: [baseResourceType],
            url: new Uri("http://ignixa.test/SearchParameter/pending-abstract-reference"))
        {
            IsSearchable = false,
        };
        context.DefinitionManager.GetSearchParameters(sourceResourceType).Returns([pending]);
        context.DefinitionManager.AllSearchParameters.Returns([pending]);
        context.DefinitionManager.GetAllKnownSearchParameters().Returns([pending]);
        context.DefinitionManager.GetAllKnownSearchParameters(sourceResourceType).Returns([pending]);
        var definitions = new SearchableSearchParameterDefinitionManager(
            context.DefinitionManager,
            () => usePartialIndices);
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => definitions, context.ValueParser, context.SchemaProvider),
            definitions);

        var options = builder.Build("Patient", [new QueryParameter(parameterName, parameterValue)]);

        options.ResolvedSearchParameters.ShouldContain(parameter => ReferenceEquals(parameter, pending));
        options.BundleIssues.ShouldContain(issue => issue.Diagnostics ==
            "Search results may be incomplete because search parameter 'pending-abstract-reference' is pending reindex.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenASystemSortWithAPendingDefinitionForOneType_WhenBuilt_ThenItUsesThePendingDefinitionRulesForEveryType(
        bool usePartialIndices)
    {
        var context = new SearchParserTestContext();
        var canonical = new Uri("http://ignixa.test/SearchParameter/custom");
        var enabled = context.Add("Patient", "custom", SearchParamType.String, url: canonical);
        var pending = context.Add("Observation", "custom", SearchParamType.String, url: canonical);
        pending.IsSearchable = false;
        var definitions = new SearchableSearchParameterDefinitionManager(
            context.DefinitionManager,
            () => usePartialIndices);
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => definitions, context.ValueParser, context.SchemaProvider),
            definitions);

        var options = builder.Build(null,
        [
            new QueryParameter("_type", "Patient,Observation"),
            new QueryParameter("_sort", "custom"),
        ]);

        options.ResolvedSearchParameters.ShouldContain(parameter => ReferenceEquals(parameter, enabled));
        if (usePartialIndices)
        {
            options.ResolvedSearchParameters.ShouldContain(parameter => ReferenceEquals(parameter, pending));
            options.Sort.ShouldHaveSingleItem().Parameter.ShouldBe(enabled);
            options.BundleIssues.ShouldContain(issue => issue.Diagnostics ==
                "Search results may be incomplete because search parameter 'custom' is pending reindex.");
        }
        else
        {
            options.Sort.ShouldBeEmpty();
            options.UnsupportedParams.ShouldContain("_sort=custom");
            options.BundleIssues.ShouldContain(issue => issue.Diagnostics ==
                "Search parameter 'custom' is pending reindex and was ignored.");
        }
    }

    [Fact]
    public void GivenASystemSortWithDifferentCanonicalDefinitions_WhenBuilt_ThenItRejectsTheRequest()
    {
        var context = new SearchParserTestContext();
        context.Add(
            "Patient",
            "custom",
            SearchParamType.String,
            url: new Uri("http://ignixa.test/SearchParameter/patient-custom"));
        context.Add(
            "Observation",
            "custom",
            SearchParamType.String,
            url: new Uri("http://ignixa.test/SearchParameter/observation-custom"));
        var definitions = new SearchableSearchParameterDefinitionManager(context.DefinitionManager);
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => definitions, context.ValueParser, context.SchemaProvider),
            definitions);

        Should.Throw<BadSearchRequestException>(() => builder.Build(null,
        [
            new QueryParameter("_type", "Patient,Observation"),
            new QueryParameter("_sort", "custom"),
        ]));
    }

    public static TheoryData<string, bool, bool, string> HiddenParameterCases()
    {
        const string ignoredPending = "Search parameter 'pending' is pending reindex and was ignored.";
        const string incompletePending =
            "Search results may be incomplete because search parameter 'pending' is pending reindex.";
        const string ignoredRedefined = "Search parameter 'pending' is being redefined and was ignored.";
        var cases = new TheoryData<string, bool, bool, string>();
        foreach (var form in new[] { "direct", "forward-chain", "reverse-chain", "include", "revinclude", "sort", "system" })
        {
            cases.Add(form, true, false, ignoredPending);
            cases.Add(form, true, true, incompletePending);
            cases.Add(form, false, false, ignoredRedefined);
            cases.Add(form, false, true, ignoredRedefined);
        }

        foreach (var form in new[] { "wildcard-include", "wildcard-revinclude" })
        {
            cases.Add(form, false, false, ignoredRedefined);
            cases.Add(form, false, true, ignoredRedefined);
        }

        return cases;
    }

    // A pending parameter is visible only with the partial-index opt-in; a redefined one never is.
    [Theory]
    [MemberData(nameof(HiddenParameterCases))]
    public void GivenAHiddenParameterInAnyResolvedPosition_WhenBuilt_ThenItReportsTheWarningForItsState(
        string form,
        bool isPending,
        bool usePartialIndices,
        string expectedDiagnostics)
    {
        var context = new SearchParserTestContext();
        var (resourceType, parameters) = AddHiddenParameter(context, form, isSupported: isPending);
        var definitions = new SearchableSearchParameterDefinitionManager(
            context.DefinitionManager,
            () => usePartialIndices);
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => definitions, context.ValueParser, context.SchemaProvider),
            definitions);

        var options = builder.Build(resourceType, parameters);

        options.BundleIssues.ShouldContain(issue => issue.Diagnostics == expectedDiagnostics);
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

    private static (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) AddHiddenParameter(
        SearchParserTestContext context,
        string form,
        bool isSupported)
    {
        SearchParameterInfo AddHidden(string resourceType, SearchParamType type, string[]? targets = null)
        {
            var pending = context.Add(resourceType, "pending", type, targets);
            pending.IsSearchable = false;
            pending.IsSupported = isSupported;
            pending.IsHiddenByTransition = !isSupported;
            return pending;
        }

        return form switch
        {
            "direct" => Direct(),
            "forward-chain" => ForwardChain(),
            "reverse-chain" => ReverseChain(),
            "include" => Include(),
            "revinclude" => RevInclude(),
            "wildcard-include" => WildcardInclude(),
            "wildcard-revinclude" => WildcardRevInclude(),
            "sort" => Sort(),

            "system" => AddSystemPendingParameter(context, isSupported),

            _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Unknown search form."),
        };

        (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) Direct()
        {
            AddHidden("Patient", SearchParamType.String);
            return ("Patient", [new QueryParameter("pending", "Smith")]);
        }

        (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) ForwardChain()
        {
            context.Add("Observation", "subject", SearchParamType.Reference, ["Patient"]);
            AddHidden("Patient", SearchParamType.String);
            return ("Observation", [new QueryParameter("subject:Patient.pending", "Smith")]);
        }

        (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) ReverseChain()
        {
            context.Add("Observation", "subject", SearchParamType.Reference, ["Patient"]);
            AddHidden("Observation", SearchParamType.String);
            return ("Patient", [new QueryParameter("_has:Observation:subject:pending", "Smith")]);
        }

        (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) Include()
        {
            AddHidden("Observation", SearchParamType.Reference, ["Patient"]);
            return ("Observation", [new QueryParameter("_include", "Observation:pending:Patient")]);
        }

        (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) RevInclude()
        {
            AddHidden("Observation", SearchParamType.Reference, ["Patient"]);
            return ("Patient", [new QueryParameter("_revinclude", "Observation:pending:Patient")]);
        }

        (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) WildcardInclude()
        {
            AddHidden("Patient", SearchParamType.Reference, ["Organization"]);
            return ("Patient", [new QueryParameter("_include", "Patient:*")]);
        }

        (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) WildcardRevInclude()
        {
            AddHidden("Observation", SearchParamType.Reference, ["Patient"]);
            return ("Patient", [new QueryParameter("_revinclude", "Observation:*")]);
        }

        (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) Sort()
        {
            AddHidden("Patient", SearchParamType.String);
            return ("Patient", [new QueryParameter("_sort", "pending")]);
        }
    }

    private static (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) AddSystemPendingParameter(
        SearchParserTestContext context,
        bool isSupported)
    {
        var pending = context.Add("Patient", "pending", SearchParamType.String);
        pending.IsSearchable = false;
        pending.IsSupported = isSupported;
        pending.IsHiddenByTransition = !isSupported;
        context.AddCommon(pending, "Patient", "Observation");

        return (null, [
            new QueryParameter("_type", "Patient,Observation"),
            new QueryParameter("pending", "Smith"),
        ]);
    }
}
