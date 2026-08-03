// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

/// <summary>
/// Drives the real parser through <see cref="SearchSqlCompiler.TryCreatePlanAsync"/>. Every other tracing test
/// hands the compiler a hand-built IR with hand-picked spans, which cannot catch a span the real scanner
/// computes wrongly, nor a real-world shape the fixtures never imagined. These assert against the actual
/// query text: a span is only useful if slicing the input with it yields the substring it claims.
/// </summary>
public class RealParserSpanDiagnosticsTests
{
    private static readonly SearchPlanOptions FullDiagnostics = new() { DiagnosticsLevel = SearchDiagnosticsLevel.Full };

    private static Task<SearchPlanResult> CompileAsync(
        SearchOptionsBuilderHarness harness,
        FakeSymbolResolver resolver,
        params (string Key, string Value)[] parameters)
        => new SearchSqlCompiler(resolver, harness.Builder).TryCreatePlanAsync(
            "Patient",
            parameters.Select(p => new QueryParameter(p.Key, p.Value)).ToList(),
            FullDiagnostics);

    // Asserts the surface it reads from: every test here expects a successful compilation, and a failure
    // still carries populated diagnostics at Full -- so falling back to result.Failure would keep the
    // assertions running against the wrong surface instead of reporting that compilation broke.
    private static SearchCompilationDiagnostics DiagnosticsOf(SearchPlanResult result)
    {
        result.Succeeded.ShouldBeTrue();
        return result.Plan!.Diagnostics.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenARealParse_WhenTraced_ThenEachSpanSlicesTheValueItClaims()
    {
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));
        var resolver = FakeSymbolResolver.For("name");

        var result = await CompileAsync(harness, resolver, ("name:exact", "Smith"));

        var parameter = DiagnosticsOf(result).Parameters.ShouldHaveSingleItem();
        var predicate = Flatten(parameter.Ir!).OfType<SearchParameterPredicateExpression>().ShouldHaveSingleItem();

        predicate.Span.ShouldNotBeNull();
        Slice(parameter, predicate.Span!.Value).ShouldBe("Smith");
    }

    [Fact]
    public async Task GivenCommaSeparatedAlternatives_WhenTraced_ThenEachAlternativeSpanSlicesItsOwnValue()
    {
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));
        var resolver = FakeSymbolResolver.For("name");

        var result = await CompileAsync(harness, resolver, ("name", "Smith,Jones"));

        var parameter = DiagnosticsOf(result).Parameters.ShouldHaveSingleItem();
        var slices = Flatten(parameter.Ir!)
            .OfType<SearchParameterPredicateExpression>()
            .Select(p => Slice(parameter, p.Span!.Value))
            .ToList();

        slices.ShouldBe(["Smith", "Jones"]);
    }

    [Fact]
    public async Task GivenAComparatorPrefix_WhenTraced_ThenTheSpanCoversThePrefixTooNotJustTheValue()
    {
        var harness = SearchOptionsBuilderHarness.ForPatient(("birthdate", SearchParamType.Date));
        var resolver = FakeSymbolResolver.For("birthdate");

        var result = await CompileAsync(harness, resolver, ("birthdate", "gt2000"));

        var parameter = DiagnosticsOf(result).Parameters.ShouldHaveSingleItem();
        var predicate = Flatten(parameter.Ir!).OfType<SearchParameterPredicateExpression>().ShouldHaveSingleItem();

        // Prefix-inclusive is load-bearing: the span is what joins a syntax node back to its IR node,
        // and a playground highlighting "2000" while the user typed "gt2000" points at the wrong thing.
        Slice(parameter, predicate.Span!.Value).ShouldBe("gt2000");
        predicate.Comparator.ShouldBe(SearchComparator.Gt);
    }

    [Fact]
    public async Task GivenTwoParametersWithSameLengthValues_WhenTraced_ThenTheirSpansGenuinelyCollide()
    {
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String), ("gender", SearchParamType.Token));
        var resolver = FakeSymbolResolver.For("name", "gender");

        var result = await CompileAsync(harness, resolver, ("gender", "male"), ("name", "abcd"));

        var spans = DiagnosticsOf(result).Parameters
            .Select(p => Flatten(p.Ir!).OfType<SearchParameterPredicateExpression>().First().Span)
            .ToList();

        // The premise behind attributing failures by parameter rather than by span: the real parser
        // really does hand two unrelated parameters the identical span.
        spans.Count.ShouldBe(2);
        spans[0].ShouldBe(spans[1]);
    }

    [Fact]
    public async Task GivenARealParse_WhenTraced_ThenBothSyntaxProjectionsAreCaptured()
    {
        var harness = SearchOptionsBuilderHarness.ForPatientChainedThrough(
            "general-practitioner", "Practitioner", "name", SearchParamType.String);
        var resolver = FakeSymbolResolver.For("general-practitioner", "name");

        var result = await CompileAsync(harness, resolver, ("general-practitioner.name", "Smith"));

        var parameter = DiagnosticsOf(result).Parameters.ShouldHaveSingleItem();
        parameter.KeySyntax.ShouldNotBeNull("the chain structure lives only on the key syntax");
        parameter.ValueSyntax.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAParameterLenientHandlingDrops_WhenTraced_ThenItIsReportedIgnoredAndTheRestStillCompiles()
    {
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String), ("birthdate", SearchParamType.Date));
        var resolver = FakeSymbolResolver.For("name", "birthdate");

        // :exact on a date is dropped by lenient handling -- the outcome the playground exists to show,
        // since nothing in the response body tells the user their parameter was silently discarded.
        var result = await CompileAsync(harness, resolver, ("name", "Smith"), ("birthdate:exact", "2000-01-01"));

        var diagnostics = DiagnosticsOf(result);

        var ignored = diagnostics.Parameters
            .Select(p => p.Outcome)
            .OfType<ParameterOutcome.Ignored>()
            .ShouldHaveSingleItem();
        ignored.Reason.ShouldNotBeNullOrWhiteSpace();

        diagnostics.Parameters.Single(p => p.Key == "name").Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        diagnostics.PlanTrace.ShouldNotBeNull("dropping one parameter must not stop the rest compiling");
    }

    [Fact]
    public async Task GivenARealParse_WhenCompiled_ThenTheChainReachesFromSpanThroughCteToSqlRange()
    {
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));
        var resolver = FakeSymbolResolver.For("name");

        var result = await CompileAsync(harness, resolver, ("name", "Smith"));

        var parameter = DiagnosticsOf(result).Parameters.ShouldHaveSingleItem();
        parameter.Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        result.Failure.ShouldBeNull();

        var planTrace = DiagnosticsOf(result).PlanTrace;
        planTrace.ShouldNotBeNull();
        var cte = planTrace!.Ctes.FirstOrDefault(c => c.ParameterOrdinal == parameter.Ordinal);
        cte.ShouldNotBeNull("no CTE was attributed to the parameter");

        var compiled = result.Plan!.Compile();
        compiled.Diagnostics.ShouldNotBeNull().SqlTextRanges.ShouldContain(r => r.Label == SqlLabels.CteLabel(cte.CteIndex));
    }

    [Fact]
    public async Task GivenARealParse_WhenTraced_ThenTheParameterCarriesItsDeclaredDataType()
    {
        // Arrange -- the binder resolved this type; recovering it downstream means walking the IR.
        var harness = SearchOptionsBuilderHarness.ForPatient(("birthdate", SearchParamType.Date));
        var resolver = FakeSymbolResolver.For("birthdate");

        // Act
        var result = await CompileAsync(harness, resolver, ("birthdate", "2020-01-01"));

        // Assert
        DiagnosticsOf(result).Parameters.ShouldHaveSingleItem().DataType.ShouldBe(SearchParamType.Date);
    }

    [Fact]
    public async Task GivenTheMatchCte_WhenTraced_ThenItsRowIsAddressableByItsCanonicalLabel()
    {
        // Arrange
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));
        var resolver = FakeSymbolResolver.For("name");

        // Act
        var result = await CompileAsync(harness, resolver, ("name", "Smith"));

        // Assert -- the match CTE displays as "root" but is emitted as cte{i}. A consumer joining a plan
        // row to its SQL range must be able to do it without knowing about that renaming.
        var planTrace = DiagnosticsOf(result).PlanTrace;
        planTrace.ShouldNotBeNull();
        var root = planTrace!.Rows.First(r => r.Label == "root");
        root.CanonicalLabel.ShouldNotBe("root");

        var compiled = result.Plan!.Compile();
        compiled.Diagnostics.ShouldNotBeNull().SqlTextRanges.ShouldContain(r => r.Label == root.CanonicalLabel);
    }

    [Fact]
    public async Task GivenTwoAndedParameters_WhenTraced_ThenTheStructuralCteReportsBothContributors()
    {
        // Arrange -- two parameters ANDed together lower to an Intersect, which by construction belongs
        // to neither one, so its ParameterOrdinal is correctly null. ContributingOrdinals is what lets a
        // consumer still say which parameters the join came from.
        var harness = SearchOptionsBuilderHarness.ForPatient(
            ("name", SearchParamType.String), ("gender", SearchParamType.Token));
        var resolver = FakeSymbolResolver.For("name", "gender");

        // Act
        var result = await CompileAsync(harness, resolver, ("name", "Smith"), ("gender", "male"));

        // Assert
        var planTrace = DiagnosticsOf(result).PlanTrace;
        planTrace.ShouldNotBeNull();
        var structural = planTrace!.Ctes.First(c => c.ParameterOrdinal is null);
        structural.ContributingOrdinals.ShouldBe([0, 1]);

        foreach (var leaf in planTrace.Ctes.Where(c => c.ParameterOrdinal is not null))
        {
            leaf.ContributingOrdinals.ShouldBe([leaf.ParameterOrdinal!.Value]);
        }
    }

    [Fact]
    public async Task GivenThreeAndedParameters_WhenTraced_ThenContributorsAccumulateThroughNestedIntersects()
    {
        // Arrange -- Lower folds ANDs left-deep: Intersect(Intersect(cte0, cte1), cte2). Reaching ordinal
        // 0 from the outermost CTE therefore takes two levels of recursion, so a walk that stopped at
        // depth 1 would still pass the two-parameter test and fail here.
        var harness = SearchOptionsBuilderHarness.ForPatient(
            ("name", SearchParamType.String),
            ("gender", SearchParamType.Token),
            ("birthdate", SearchParamType.Date));
        var resolver = FakeSymbolResolver.For("name", "gender", "birthdate");

        // Act
        var result = await CompileAsync(
            harness, resolver, ("name", "Smith"), ("gender", "male"), ("birthdate", "2020-01-01"));

        // Assert
        var planTrace = DiagnosticsOf(result).PlanTrace;
        planTrace.ShouldNotBeNull();
        var structural = planTrace!.Ctes.Where(c => c.ParameterOrdinal is null).ToList();
        structural.Count.ShouldBeGreaterThanOrEqualTo(2, "a three-way AND should nest intersects");

        var outermost = planTrace.Ctes[planTrace.Ctes.Count - 1];
        outermost.ContributingOrdinals.ShouldBe([0, 1, 2]);
    }

    private static string Slice(ParameterTrace parameter, SourceSpan span)
    {
        var source = span.Origin == SourceOrigin.Key ? parameter.Key : parameter.Value;
        span.Start.ShouldBeLessThanOrEqualTo(source.Length - span.Length);
        return source.Substring(span.Start, span.Length);
    }

    private static IEnumerable<Expression> Flatten(Expression node)
    {
        yield return node;

        IReadOnlyList<Expression> children = node switch
        {
            MultiaryExpression m => m.Expressions,
            UnionExpression u => u.Expressions,
            NotExpression n => [n.Expression],
            SearchParameterExpression sp => [sp.Expression],
            ChainedExpression c => [c.Expression],
            CompositeComponentExpression cc => [cc.WrappedExpression],
            _ => [],
        };

        foreach (var child in children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class FakeSymbolResolver : ISymbolResolver
    {
        private readonly Dictionary<string, short> _searchParamIds = [];

        public static FakeSymbolResolver For(params string[] codes)
        {
            var resolver = new FakeSymbolResolver();
            short next = 100;
            foreach (var code in codes)
            {
                resolver._searchParamIds[code] = next++;
            }

            return resolver;
        }

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult(_searchParamIds.TryGetValue(parameter.Code, out var id) ? (short?)id : null);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult<short?>(resourceType == "Patient" ? (short)103 : (short)105);

        public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
            => Task.FromResult<int?>(null);

        public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult<int?>(null);
    }
}
