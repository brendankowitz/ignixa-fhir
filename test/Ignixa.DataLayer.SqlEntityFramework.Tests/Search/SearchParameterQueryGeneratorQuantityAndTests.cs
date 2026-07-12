// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Regression tests for GenerateQuantityAndQueryAsync's value-comparator extraction. Expressions are
/// built via the real SearchParameterExpressionParser to prove the shape production REST searches
/// produce: eq/ap/ne widen to a nested MultiaryExpression pair rather than a single BinaryExpression,
/// which the extraction previously only matched at the top level of the outer AND, silently dropping
/// the value filter (or, for the no-unit case, keeping only the last of the two bounds).
/// </summary>
public class SearchParameterQueryGeneratorQuantityAndTests : TestBase
{
    private const short ObservationTypeId = 3;
    private const short ValueQuantityParamId = 7;
    private const string ValueQuantityUrl = "http://hl7.org/fhir/SearchParameter/Observation-value-quantity";
    private const string Ucum = "http://unitsofmeasure.org";

    private readonly SearchParameterQueryGenerator _generator;
    private readonly SearchParameterExpressionParser _parser;
    private readonly SearchParameterInfo _valueQuantityParam;

    public SearchParameterQueryGeneratorQuantityAndTests()
    {
        var compositeGenerator = new CompositeSearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());

        _generator = new SearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<SearchParameterQueryGenerator>(),
            compositeGenerator);

        _parser = new SearchParameterExpressionParser(
            Substitute.For<IReferenceSearchValueParser>(),
            Substitute.For<IFhirSchemaProvider>());

        Context.SearchParams.Add(new SearchParamEntity
        {
            SearchParamId = ValueQuantityParamId,
            Uri = ValueQuantityUrl,
            Status = "Enabled",
            LastUpdated = DateTimeOffset.UtcNow
        });
        Context.SaveChanges();

        _valueQuantityParam = new SearchParameterInfo(
            "value-quantity",
            "value-quantity",
            SearchParamType.Quantity,
            new Uri(ValueQuantityUrl));
    }

    private async Task<long> CreateObservationWithQuantityAsync(string resourceId, decimal value, string? system, string? code)
    {
        var resource = CreateResource(ObservationTypeId, resourceId);

        int? systemId = system is null ? null : await Cache.GetOrCreateSystemIdAsync(system);
        int? codeId = code is null ? null : await Cache.GetOrCreateQuantityCodeIdAsync(code);

        Context.QuantitySearchParams.Add(new QuantitySearchParamEntity
        {
            ResourceTypeId = ObservationTypeId,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = ValueQuantityParamId,
            SystemId = systemId,
            QuantityCodeId = codeId,
            SingleValue = value,
            LowValue = value,
            HighValue = value
        });
        Context.SaveChanges();

        return resource.ResourceSurrogateId;
    }

    private async Task<long> CreateObservationWithQuantityRangeAsync(string resourceId, decimal low, decimal high, string? system, string? code)
    {
        var resource = CreateResource(ObservationTypeId, resourceId);

        int? systemId = system is null ? null : await Cache.GetOrCreateSystemIdAsync(system);
        int? codeId = code is null ? null : await Cache.GetOrCreateQuantityCodeIdAsync(code);

        Context.QuantitySearchParams.Add(new QuantitySearchParamEntity
        {
            ResourceTypeId = ObservationTypeId,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = ValueQuantityParamId,
            SystemId = systemId,
            QuantityCodeId = codeId,
            SingleValue = null,
            LowValue = low,
            HighValue = high
        });
        Context.SaveChanges();

        return resource.ResourceSurrogateId;
    }

    private async Task<List<long>> RunSearchAsync(string queryValue)
    {
        var expression = (SearchParameterExpression)_parser.Parse(_valueQuantityParam, modifier: null, queryValue);
        var query = await _generator.GenerateQueryAsync(ObservationTypeId, expression, CancellationToken.None);
        return await query.ToListAsync();
    }

    [Fact]
    public async Task GivenUnitQualifiedEqQuantitySearch_WhenGeneratingQuery_ThenOnlyMatchingValueIsReturned()
    {
        var matching = await CreateObservationWithQuantityAsync("obs-5.4", 5.4m, Ucum, "mg");
        await CreateObservationWithQuantityAsync("obs-999", 999m, Ucum, "mg");

        var results = await RunSearchAsync($"5.4|{Ucum}|mg");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenBareEqQuantitySearch_WhenGeneratingQuery_ThenLowerBoundIsApplied()
    {
        var matching = await CreateObservationWithQuantityAsync("obs-5.4", 5.4m, Ucum, "mg");
        await CreateObservationWithQuantityAsync("obs-1.0", 1.0m, Ucum, "mg");

        var results = await RunSearchAsync("5.4");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenUnitQualifiedNeQuantitySearch_WhenGeneratingQuery_ThenMatchingValueIsExcluded()
    {
        await CreateObservationWithQuantityAsync("obs-5.4", 5.4m, Ucum, "mg");
        var nonMatching = await CreateObservationWithQuantityAsync("obs-999", 999m, Ucum, "mg");

        var results = await RunSearchAsync($"ne5.4|{Ucum}|mg");

        results.ShouldBe(new[] { nonMatching });
    }

    [Fact]
    public async Task GivenUnitQualifiedLtQuantitySearch_WhenGeneratingQuery_ThenOnlyLesserValueIsReturned()
    {
        var lesser = await CreateObservationWithQuantityAsync("obs-1.0", 1.0m, Ucum, "mg");
        await CreateObservationWithQuantityAsync("obs-999", 999m, Ucum, "mg");

        var results = await RunSearchAsync($"lt5.4|{Ucum}|mg");

        results.ShouldBe(new[] { lesser });
    }

    [Fact]
    public async Task GivenUnitQualifiedApQuantitySearch_WhenGeneratingQuery_ThenOnlyApproximateValueIsReturned()
    {
        var matching = await CreateObservationWithQuantityAsync("obs-5.4", 5.4m, Ucum, "mg");
        await CreateObservationWithQuantityAsync("obs-999", 999m, Ucum, "mg");

        var results = await RunSearchAsync($"ap5.4|{Ucum}|mg");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenUnitQualifiedSaQuantitySearch_WhenGeneratingQuery_ThenExcludesStraddlingRange()
    {
        // Stored range [5.0, 6.0] straddles the search boundary of 5.4: gt (overlap-above) would
        // match it (HighValue 6.0 > 5.4), but sa (strictly after, no overlap) must not
        // (LowValue 5.0 > 5.4 is false) - exactly the distinction lost by aliasing sa to gt.
        await CreateObservationWithQuantityRangeAsync("obs-straddling", 5.0m, 6.0m, Ucum, "mg");
        var clearlyAfter = await CreateObservationWithQuantityRangeAsync("obs-clearly-after", 10.0m, 10.0m, Ucum, "mg");

        var results = await RunSearchAsync($"sa5.4|{Ucum}|mg");

        results.ShouldBe(new[] { clearlyAfter });
    }

    [Fact]
    public async Task GivenUnitQualifiedEbQuantitySearch_WhenGeneratingQuery_ThenExcludesStraddlingRange()
    {
        // Stored range [5.0, 6.0] straddles the search boundary of 5.4: lt (overlap-below) would
        // match it (LowValue 5.0 < 5.4), but eb (strictly before, no overlap) must not
        // (HighValue 6.0 < 5.4 is false).
        await CreateObservationWithQuantityRangeAsync("obs-straddling", 5.0m, 6.0m, Ucum, "mg");
        var clearlyBefore = await CreateObservationWithQuantityRangeAsync("obs-clearly-before", 1.0m, 1.0m, Ucum, "mg");

        var results = await RunSearchAsync($"eb5.4|{Ucum}|mg");

        results.ShouldBe(new[] { clearlyBefore });
    }
}
