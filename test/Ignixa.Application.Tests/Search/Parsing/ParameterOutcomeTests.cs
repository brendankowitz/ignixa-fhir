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
    public static TheoryData<string, string, string, string, bool> WildcardIncludeCases()
    {
        var cases = new TheoryData<string, string, string, string, bool>();
        foreach (var (parameterName, sourceResourceType) in new[] { ("_include", "Patient"), ("_revinclude", "Observation") })
        {
            foreach (var usePartialIndices in new[] { false, true })
            {
                cases.Add(parameterName, "*", sourceResourceType, sourceResourceType, usePartialIndices);
                foreach (var baseResourceType in new[] { sourceResourceType, "Resource", "DomainResource" })
                {
                    cases.Add(parameterName, $"{sourceResourceType}:*", sourceResourceType, baseResourceType, usePartialIndices);
                }
            }
        }

        return cases;
    }

    // A wildcard include expands to pending references too, concrete or declared on an abstract base.
    [Theory]
    [MemberData(nameof(WildcardIncludeCases))]
    public void GivenAWildcardIncludeWithAPendingReference_WhenBuilt_ThenItWarnsOfIncompleteResultsAndTracksTheParameter(
        string parameterName,
        string parameterValue,
        string sourceResourceType,
        string baseResourceType,
        bool usePartialIndices)
    {
        var context = new SearchParserTestContext();
        var pending = new SearchParameterInfo("pending-reference", "pending-reference", SearchParamType.Reference, targetResourceTypes: ["Organization", "Patient"],
            baseResourceTypes: [baseResourceType], url: new Uri("http://ignixa.test/SearchParameter/pending-reference")) { IsSearchable = false };
        context.DefinitionManager.GetSearchParameters(sourceResourceType).Returns([pending]);
        context.DefinitionManager.AllSearchParameters.Returns([pending]);
        context.DefinitionManager.GetAllKnownSearchParameters().Returns([pending]);
        context.DefinitionManager.GetAllKnownSearchParameters(sourceResourceType).Returns([pending]);

        var options = Build(context, usePartialIndices, "Patient", [new QueryParameter(parameterName, parameterValue)]);

        options.ResolvedSearchParameters.ShouldContain(parameter => ReferenceEquals(parameter, pending));
        options.BundleIssues.ShouldContain(issue => issue.Diagnostics ==
            "Search results may be incomplete because search parameter 'pending-reference' is pending reindex.");
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

        var options = Build(context, usePartialIndices, null, [new("_type", "Patient,Observation"), new("_sort", "custom")]);

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
        context.Add("Patient", "custom", SearchParamType.String, url: new Uri("http://ignixa.test/SearchParameter/patient-custom"));
        context.Add("Observation", "custom", SearchParamType.String, url: new Uri("http://ignixa.test/SearchParameter/observation-custom"));

        Should.Throw<BadSearchRequestException>(() =>
            Build(context, usePartialIndices: false, null, [new("_type", "Patient,Observation"), new("_sort", "custom")]));
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

        var options = Build(context, usePartialIndices, resourceType, parameters);

        options.BundleIssues.ShouldContain(issue => issue.Diagnostics == expectedDiagnostics);
    }

    // The trace shows why a hidden parameter did not reach the query, as it does for an unsupported one.
    [Fact]
    public void GivenAHiddenParameter_WhenTraced_ThenItIsReportedIgnoredAndTracked()
    {
        var context = new SearchParserTestContext();
        var (resourceType, parameters) = AddHiddenParameter(context, "direct", isSupported: true);
        var outcomes = new List<ParameterTrace>();

        var options = Build(context, usePartialIndices: false, resourceType, parameters, outcomes);

        var trace = outcomes.ShouldHaveSingleItem();
        trace.Key.ShouldBe(parameters.ShouldHaveSingleItem().Name);
        trace.Outcome.ShouldBeOfType<ParameterOutcome.Ignored>().Reason.ShouldNotBeNullOrWhiteSpace();
        options.HiddenSearchParameters.ShouldContainKey(trace.Key);
    }

    [Theory]
    [InlineData("birthdate", SearchParamType.Date, "birthdate:exact", "2000-01-01", typeof(ParameterOutcome.Ignored))]
    [InlineData("name", SearchParamType.String, "name", "Smith", typeof(ParameterOutcome.Compiled))]
    public void GivenAParameter_WhenBuilt_ThenItsTraceReportsWhetherItCompiledOrWasIgnored(
        string code,
        SearchParamType type,
        string key,
        string value,
        Type expectedOutcome)
    {
        var outcomes = new List<ParameterTrace>();

        SearchOptionsBuilderHarness.ForPatient((code, type)).Build([(key, value)], outcomes);

        var trace = outcomes.ShouldHaveSingleItem();
        trace.Key.ShouldBe(key);
        trace.Outcome.ShouldBeOfType(expectedOutcome);
    }

    [Fact]
    public void GivenAChainedKey_WhenBuilt_ThenKeySyntaxRetainsTheChainStructure()
    {
        var harness = SearchOptionsBuilderHarness.ForPatientChainedThrough("general-practitioner", "Practitioner", "name", SearchParamType.String);
        var outcomes = new List<ParameterTrace>();

        harness.Build([("general-practitioner.name", "Smith")], outcomes);

        var trace = outcomes.ShouldHaveSingleItem();
        trace.Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        trace.KeySyntax.ShouldNotBeNull().Kind.ShouldBe("ForwardChain");
        trace.KeySyntax.Children.ShouldHaveSingleItem();
    }

    private static SearchOptions Build(
        SearchParserTestContext context,
        bool usePartialIndices,
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        IList<ParameterTrace>? outcomes = null)
    {
        var definitions = new SearchableSearchParameterDefinitionManager(context.DefinitionManager, () => usePartialIndices);
        return new SearchOptionsBuilder(new ExpressionParser(() => definitions, context.ValueParser, context.SchemaProvider), definitions)
            .Build(resourceType, parameters, outcomes: outcomes);
    }

    // Registers a hidden "pending" parameter where the search form resolves it, and returns that form's query.
    private static (string? ResourceType, IReadOnlyList<QueryParameter> Parameters) AddHiddenParameter(
        SearchParserTestContext context,
        string form,
        bool isSupported)
    {
        (string HiddenOn, SearchParamType Type, string[]? Targets, string? ResourceType, string Name, string Value) query = form switch
        {
            "direct" => ("Patient", SearchParamType.String, null, "Patient", "pending", "Smith"),
            "forward-chain" => ("Patient", SearchParamType.String, null, "Observation", "subject:Patient.pending", "Smith"),
            "reverse-chain" => ("Observation", SearchParamType.String, null, "Patient", "_has:Observation:subject:pending", "Smith"),
            "include" => ("Observation", SearchParamType.Reference, ["Patient"], "Observation", "_include", "Observation:pending:Patient"),
            "revinclude" => ("Observation", SearchParamType.Reference, ["Patient"], "Patient", "_revinclude", "Observation:pending:Patient"),
            "wildcard-include" => ("Patient", SearchParamType.Reference, ["Organization"], "Patient", "_include", "Patient:*"),
            "wildcard-revinclude" => ("Observation", SearchParamType.Reference, ["Patient"], "Patient", "_revinclude", "Observation:*"),
            "sort" => ("Patient", SearchParamType.String, null, "Patient", "_sort", "pending"),
            "system" => ("Patient", SearchParamType.String, null, null, "pending", "Smith"),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Unknown search form."),
        };
        if (form is "forward-chain" or "reverse-chain")
        {
            context.Add("Observation", "subject", SearchParamType.Reference, ["Patient"]);
        }

        var pending = context.Add(query.HiddenOn, "pending", query.Type, query.Targets);
        pending.IsSearchable = false;
        pending.IsSupported = isSupported;
        pending.IsHiddenByTransition = !isSupported;
        if (form == "system")
        {
            context.AddCommon(pending, "Patient", "Observation");
            return (null, [new QueryParameter("_type", "Patient,Observation"), new QueryParameter("pending", "Smith")]);
        }

        return (query.ResourceType, [new QueryParameter(query.Name, query.Value)]);
    }
}
