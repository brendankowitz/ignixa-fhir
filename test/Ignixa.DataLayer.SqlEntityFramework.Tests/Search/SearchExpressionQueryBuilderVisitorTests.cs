// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Confirms SearchExpressionQueryBuilder's IExpressionVisitor-based dispatch (Task 5 of the SQL
/// data layer cleanup plan) produces identical results to the pre-refactor expression-switch
/// dispatch for representative expression shapes, including nested recursion through AcceptVisitor.
/// Uses Number-typed search parameters rather than String: the String path routes through
/// EF.Functions.Collate, which the EF Core InMemory provider used by TestBase cannot translate
/// (a pre-existing, unrelated provider limitation, not a Task 5 regression).
/// </summary>
public class SearchExpressionQueryBuilderVisitorTests : TestBase
{
    private const short NumberSearchParamId = 100;

    private readonly SearchExpressionQueryBuilder _builder;

    public SearchExpressionQueryBuilderVisitorTests()
    {
        var compositeGenerator = new CompositeSearchParameterQueryGenerator(
            Context, Cache, LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());
        var parameterGenerator = new SearchParameterQueryGenerator(
            Context, Cache, LoggerFactory.CreateLogger<SearchParameterQueryGenerator>(), compositeGenerator);
        var chainedProcessor = new ChainedExpressionProcessor(
            Context, Cache, parameterGenerator, LoggerFactory.CreateLogger<ChainedExpressionProcessor>());
        var compartmentGenerator = new CompartmentSearchQueryGenerator(
            Context,
            Cache,
            Substitute.For<ICompartmentDefinitionManager>(),
            Substitute.For<ISearchParameterDefinitionManager>(),
            LoggerFactory.CreateLogger<CompartmentSearchQueryGenerator>());
        var patientEverythingGenerator = new PatientEverythingQueryGenerator(
            Context, compartmentGenerator, LoggerFactory.CreateLogger<PatientEverythingQueryGenerator>());

        _builder = new SearchExpressionQueryBuilder(
            Context,
            parameterGenerator,
            chainedProcessor,
            compartmentGenerator,
            patientEverythingGenerator,
            Substitute.For<ISearchParameterDefinitionManager>(),
            LoggerFactory.CreateLogger<SearchExpressionQueryBuilder>());

        Context.SearchParams.Add(new SearchParamEntity
        {
            SearchParamId = NumberSearchParamId,
            Uri = "http://hl7.org/fhir/SearchParameter/Patient-test-number",
            Status = "Enabled",
            LastUpdated = DateTimeOffset.UtcNow,
        });
        Context.SaveChanges();
    }

    private void CreateNumberSearchParam(long resourceSurrogateId, short resourceTypeId, decimal value)
    {
        Context.NumberSearchParams.Add(new NumberSearchParamEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceSurrogateId = resourceSurrogateId,
            SearchParamId = NumberSearchParamId,
            LowValue = value,
            HighValue = value,
        });
        Context.SaveChanges();
    }

    [Fact]
    public async Task GivenSingleSearchParameterExpression_WhenApplied_ThenReturnsMatchingResource()
    {
        // Arrange
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateNumberSearchParam(patient.ResourceSurrogateId, resourceTypeId: 1, value: 42m);

        var expression = new SearchParameterExpression(
            new SearchParameterInfo("test-number", "test-number", SearchParamType.Number, url: new Uri("http://hl7.org/fhir/SearchParameter/Patient-test-number")),
            new BinaryExpression(BinaryOperator.Equal, FieldName.Number, null, 42m));

        // Act
        var result = await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, expression, CancellationToken.None);
        var results = await result.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ResourceSurrogateId.ShouldBe(patient.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenNestedMultiaryOfMultiary_WhenApplied_ThenRecursesThroughAcceptVisitorCorrectly()
    {
        // Arrange: AND(OR(value=42, value=84), _type=Patient) exercises AcceptVisitor recursion
        // through two nested MultiaryExpression levels (Step 3's dispatch-via-AcceptVisitor change) —
        // the AND and OR are both top-level Expression nodes dispatched via VisitMultiary/AcceptVisitor.
        var smith = CreateResource(resourceTypeId: 1, resourceId: "patient-smith");
        CreateNumberSearchParam(smith.ResourceSurrogateId, resourceTypeId: 1, value: 42m);
        var other = CreateResource(resourceTypeId: 1, resourceId: "patient-other");
        CreateNumberSearchParam(other.ResourceSurrogateId, resourceTypeId: 1, value: 999m);

        var numberParameter = new SearchParameterInfo("test-number", "test-number", SearchParamType.Number, url: new Uri("http://hl7.org/fhir/SearchParameter/Patient-test-number"));
        var orExpression = new MultiaryExpression(
            MultiaryOperator.Or,
            new Expression[]
            {
                new SearchParameterExpression(numberParameter, new BinaryExpression(BinaryOperator.Equal, FieldName.Number, null, 42m)),
                new SearchParameterExpression(numberParameter, new BinaryExpression(BinaryOperator.Equal, FieldName.Number, null, 84m)),
            });

        var typeParameter = new SearchParameterInfo("_type", "_type", SearchParamType.Token);
        var typeExpression = new SearchParameterExpression(
            typeParameter,
            new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "Patient", false));

        var andExpression = new MultiaryExpression(MultiaryOperator.And, new Expression[] { orExpression, typeExpression });

        // Act
        var result = await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, andExpression, CancellationToken.None);
        var results = await result.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ResourceSurrogateId.ShouldBe(smith.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenBareBinaryExpression_WhenApplied_ThenThrowsNotSupported()
    {
        // Arrange: proves VisitBinary (one of six explicit-interface stub members from Step 4) is
        // wired up, not silently returning null/default. The other five are covered by the tests below.
        var expression = new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeStart, null, DateTime.UtcNow);

        // Act & Assert
        await Should.ThrowAsync<NotSupportedException>(async () =>
            await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, expression, CancellationToken.None));
    }

    [Fact]
    public async Task GivenBareMissingFieldExpression_WhenApplied_ThenThrowsNotSupported()
    {
        var expression = new MissingFieldExpression(FieldName.TokenCode, null);

        await Should.ThrowAsync<NotSupportedException>(async () =>
            await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, expression, CancellationToken.None));
    }

    [Fact]
    public async Task GivenBareStringExpression_WhenApplied_ThenThrowsNotSupported()
    {
        var expression = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "Patient", false);

        await Should.ThrowAsync<NotSupportedException>(async () =>
            await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, expression, CancellationToken.None));
    }

    [Fact]
    public async Task GivenIncludeExpression_WhenApplied_ThenThrowsNotSupported()
    {
        var expression = new IncludeExpression(
            new[] { "Patient" }, null, "Patient", null, Array.Empty<string>(), wildCard: true, reversed: false, iterate: false);

        await Should.ThrowAsync<NotSupportedException>(async () =>
            await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, expression, CancellationToken.None));
    }

    [Fact]
    public async Task GivenSortExpression_WhenApplied_ThenThrowsNotSupported()
    {
        var expression = new SortExpression(new SearchParameterInfo("name", "name", SearchParamType.String));

        await Should.ThrowAsync<NotSupportedException>(async () =>
            await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, expression, CancellationToken.None));
    }

    [Fact]
    public async Task GivenBareInExpression_WhenApplied_ThenThrowsNotSupported()
    {
        var expression = new InExpression<string>(FieldName.TokenCode, null, new[] { "a", "b" });

        await Should.ThrowAsync<NotSupportedException>(async () =>
            await _builder.ApplySearchExpressionAsync(Context.Resources, resourceTypeId: 1, expression, CancellationToken.None));
    }
}
