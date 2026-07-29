// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

/// <summary>
/// Drives the real <see cref="SearchOptionsBuilder"/>, not hand-built options: the reported implicit values
/// are only worth anything if they track whatever the builder actually resolves, so the builder has to be
/// the thing under test alongside the reporting.
/// </summary>
public class ImplicitParameterDiagnosticsTests
{
    private static readonly SearchPlanOptions FullDiagnostics = new() { DiagnosticsLevel = SearchDiagnosticsLevel.Full };

    [Fact]
    public async Task GivenNoCount_WhenCompiled_ThenCountIsReportedImplicitWithTheValueTheBuilderResolved()
    {
        // Arrange
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));
        var expected = harness.Build([("name", "Smith")]).MaxItemCount;

        // Act
        var result = await CompileAsync(harness, ("name", "Smith"));

        // Assert
        var count = DiagnosticsOf(result).Implicit.ShouldHaveSingleItem();
        count.Name.ShouldBe("_count");
        expected.ShouldBeGreaterThan(0);
        count.Value.ShouldBe(expected.ToString(CultureInfo.InvariantCulture));
        count.Reason.ShouldBe("server default");
    }

    [Fact]
    public async Task GivenAnExplicitCount_WhenCompiled_ThenNoImplicitCountIsReported()
    {
        // Arrange
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));

        // Act
        var result = await CompileAsync(harness, ("name", "Smith"), ("_count", "25"));

        // Assert
        DiagnosticsOf(result).Implicit.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenSummaryCountWithoutTotal_WhenCompiled_ThenTotalIsReportedAsImpliedBySummaryRatherThanADefault()
    {
        // Arrange
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));

        // Act
        var result = await CompileAsync(harness, ("name", "Smith"), ("_summary", "count"));

        // Assert
        var total = DiagnosticsOf(result).Implicit.Single(i => i.Name == "_total");
        total.Value.ShouldBe(nameof(TotalType.Accurate));
        total.Reason.ShouldBe("implied by _summary=count");
    }

    [Fact]
    public async Task GivenSummaryCountAndAnExplicitTotal_WhenCompiled_ThenTotalIsNotReportedImplicit()
    {
        // Arrange
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));

        // Act
        var result = await CompileAsync(harness, ("name", "Smith"), ("_summary", "count"), ("_total", "accurate"));

        // Assert
        DiagnosticsOf(result).Implicit.ShouldNotContain(i => i.Name == "_total");
    }

    [Fact]
    public async Task GivenNoSummaryOrTotal_WhenCompiled_ThenNeitherIsReportedBecauseNeitherResolvedToADecision()
    {
        // Arrange
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));

        // Act
        var result = await CompileAsync(harness, ("name", "Smith"));

        // Assert
        DiagnosticsOf(result).Implicit.Select(i => i.Name).ShouldBe(["_count"]);
    }

    [Fact]
    public async Task GivenACountWithAModifierSuffix_WhenCompiled_ThenItIsStillTreatedAsSupplied()
    {
        // Arrange -- the builder classifies on the name before the colon, so the reporting must too.
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));

        // Act
        var result = await CompileAsync(harness, ("name", "Smith"), ("_count:exact", "25"));

        // Assert
        DiagnosticsOf(result).Implicit.ShouldNotContain(i => i.Name == "_count");
    }

    private static Task<SearchPlanResult> CompileAsync(
        SearchOptionsBuilderHarness harness,
        params (string Key, string Value)[] parameters)
        => new SearchSqlCompiler(FakeSymbolResolver.Instance, harness.Builder).TryCreatePlanAsync(
            "Patient",
            parameters.Select(p => new QueryParameter(p.Key, p.Value)).ToList(),
            FullDiagnostics);

    private static SearchCompilationDiagnostics DiagnosticsOf(SearchPlanResult result)
        => (result.Plan?.Diagnostics ?? result.Failure?.Diagnostics)!;

    private sealed class FakeSymbolResolver : ISymbolResolver
    {
        public static readonly FakeSymbolResolver Instance = new();

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult<short?>(202);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult<short?>(103);

        public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
            => Task.FromResult<int?>(null);

        public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult<int?>(null);
    }
}
