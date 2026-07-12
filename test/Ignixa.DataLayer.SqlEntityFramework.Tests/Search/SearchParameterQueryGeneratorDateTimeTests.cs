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
/// Covers GenerateDateTimeQuery - the standalone (non-multiary) DateTime BinaryExpression path,
/// which historically maintained its own (FieldName, BinaryOperator) switch independent of
/// ComparisonPredicates. Confirms the unification onto BuildSingleConditionDateTimeQuery preserves
/// existing behavior and correctly wires the new StartsAfter/EndsBefore operators.
/// </summary>
public class SearchParameterQueryGeneratorDateTimeTests : TestBase
{
    private const short ObservationTypeId = 3;
    private const short DateParamId = 8;
    private const string DateParamUrl = "http://hl7.org/fhir/SearchParameter/Observation-date";

    private readonly SearchParameterQueryGenerator _generator;
    private readonly SearchParameterExpressionParser _parser;
    private readonly SearchParameterInfo _dateParam;

    public SearchParameterQueryGeneratorDateTimeTests()
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
            SearchParamId = DateParamId,
            Uri = DateParamUrl,
            Status = "Enabled",
            LastUpdated = DateTimeOffset.UtcNow
        });
        Context.SaveChanges();

        _dateParam = new SearchParameterInfo("date", "date", SearchParamType.Date, new Uri(DateParamUrl));
    }

    private async Task<long> CreateObservationWithDateAsync(string resourceId, DateTime start, DateTime end)
    {
        var resource = CreateResource(ObservationTypeId, resourceId);

        Context.DateTimeSearchParams.Add(new DateTimeSearchParamEntity
        {
            ResourceTypeId = ObservationTypeId,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = DateParamId,
            StartDateTime = start,
            EndDateTime = end,
            IsLongerThanADay = false,
            IsMin = false,
            IsMax = false
        });
        Context.SaveChanges();

        return resource.ResourceSurrogateId;
    }

    private async Task<List<long>> RunSearchAsync(string queryValue)
    {
        var expression = (SearchParameterExpression)_parser.Parse(_dateParam, modifier: null, queryValue);
        var query = await _generator.GenerateQueryAsync(ObservationTypeId, expression, CancellationToken.None);
        return await query.ToListAsync();
    }

    [Fact]
    public async Task GivenBareGtDateSearch_WhenGeneratingQuery_ThenUnificationPreservesExistingBehavior()
    {
        var matching = await CreateObservationWithDateAsync("obs-late", new DateTime(2020, 6, 1), new DateTime(2020, 6, 1));
        await CreateObservationWithDateAsync("obs-early", new DateTime(2019, 1, 1), new DateTime(2019, 1, 1));

        var results = await RunSearchAsync("gt2020-01-01");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenBareSaDateSearch_WhenGeneratingQuery_ThenAppliesStrictAfterSemantics()
    {
        var matching = await CreateObservationWithDateAsync("obs-clearly-after", new DateTime(2020, 6, 1), new DateTime(2020, 6, 1));
        await CreateObservationWithDateAsync("obs-overlapping-year", new DateTime(2020, 1, 1), new DateTime(2020, 12, 31));

        var results = await RunSearchAsync("sa2020-01-01");

        // "obs-overlapping-year" is a whole-year-precision value straddling the search boundary -
        // sa (strictly after, ignoring precision widening) must exclude it, unlike gt's overlap test.
        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenBareEbDateSearch_WhenGeneratingQuery_ThenAppliesStrictBeforeSemantics()
    {
        var matching = await CreateObservationWithDateAsync("obs-clearly-before", new DateTime(2018, 1, 1), new DateTime(2018, 1, 1));
        await CreateObservationWithDateAsync("obs-overlapping-year", new DateTime(2019, 1, 1), new DateTime(2019, 12, 31));

        var results = await RunSearchAsync("eb2019-06-15");

        results.ShouldBe(new[] { matching });
    }
}
