// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Pins that the query path only reads terminology. A search naming a system or unit code that has no row
/// must return no matches and leave the database unchanged: creating the row would write on a GET, and would
/// also make the row exist for the "known miss" diagnostic that follows, hiding the typo it was added to
/// report.
/// </summary>
public sealed class SearchParameterQueryGeneratorTerminologyTests : IDisposable
{
    private const string ObservationCodeParameterUri = "http://hl7.org/fhir/SearchParameter/Observation-code";
    private const string ObservationValueQuantityParameterUri = "http://hl7.org/fhir/SearchParameter/Observation-value-quantity";

    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly SearchParameterQueryGenerator _generator;

    public SearchParameterQueryGeneratorTerminologyTests()
    {
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FhirDbContext(options);
        _cache = new SearchIndexReferenceDataCache(_context, NullLogger<SearchIndexReferenceDataCache>.Instance);

        _context.ResourceTypes.Add(new ResourceTypeEntity { ResourceTypeId = 3, Name = "Observation" });
        _context.SearchParams.AddRange(
            new SearchParamEntity { SearchParamId = 4, Uri = ObservationCodeParameterUri, Status = "Enabled" },
            new SearchParamEntity { SearchParamId = 5, Uri = ObservationValueQuantityParameterUri, Status = "Enabled" });
        _context.SaveChanges();

        _generator = new SearchParameterQueryGenerator(
            _context,
            _cache,
            NullLogger<SearchParameterQueryGenerator>.Instance,
            new CompositeSearchParameterQueryGenerator(
                _context,
                _cache,
                NullLogger<CompositeSearchParameterQueryGenerator>.Instance));
    }

    [Fact]
    public async Task GivenATokenSystemWithNoRow_WhenGeneratingAQuery_ThenCreatesNothingAndMatchesNothing()
    {
        // Arrange
        const string systemUri = "http://typo.example/loinc";
        var expression = new SearchParameterExpression(
            TokenParameter(),
            new StringExpression(StringOperator.Equals, FieldName.TokenSystem, componentIndex: null, systemUri, ignoreCase: false));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 3, expression, CancellationToken.None);

        // Assert
        _context.Systems.Any(s => s.Value == systemUri).ShouldBeFalse("the query path must not create terminology rows");
        query.ToList().ShouldBeEmpty("an unknown system cannot match any indexed token");
    }

    [Fact]
    public async Task GivenATokenSystemWithNoRow_WhenGeneratingAQuery_ThenTheSystemFilterIsNotDropped()
    {
        // Arrange: a token row under a *different* system. Losing the system filter would return it.
        const string missingSystem = "http://typo.example/loinc";
        var indexedSystem = new SystemEntity { Value = "http://loinc.org" };
        _context.Systems.Add(indexedSystem);
        await _context.SaveChangesAsync();

        _context.TokenSearchParams.Add(new TokenSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = 1000,
            SearchParamId = 4,
            SystemId = indexedSystem.SystemId,
            Code = "1234-5"
        });
        await _context.SaveChangesAsync();

        var expression = new SearchParameterExpression(
            TokenParameter(),
            new StringExpression(StringOperator.Equals, FieldName.TokenSystem, componentIndex: null, missingSystem, ignoreCase: false));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 3, expression, CancellationToken.None);

        // Assert
        query.ToList().ShouldBeEmpty("a system with no row must narrow the match set to nothing, never widen it");
    }

    [Fact]
    public async Task GivenAQuantityCodeWithNoRow_WhenGeneratingAQuery_ThenCreatesNothingAndMatchesNothing()
    {
        // Arrange
        const string code = "typo-unit";
        var expression = new SearchParameterExpression(
            QuantityParameter(),
            new StringExpression(StringOperator.Equals, FieldName.QuantityCode, componentIndex: null, code, ignoreCase: false));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 3, expression, CancellationToken.None);

        // Assert
        _context.QuantityCodes.Any(qc => qc.Value == code).ShouldBeFalse("the query path must not create terminology rows");
        query.ToList().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAQuantitySystemWithNoRow_WhenGeneratingAQuery_ThenCreatesNothingAndMatchesNothing()
    {
        // Arrange
        const string systemUri = "http://typo.example/unitsofmeasure";
        var expression = new SearchParameterExpression(
            QuantityParameter(),
            new StringExpression(StringOperator.Equals, FieldName.QuantitySystem, componentIndex: null, systemUri, ignoreCase: false));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 3, expression, CancellationToken.None);

        // Assert
        _context.Systems.Any(s => s.Value == systemUri).ShouldBeFalse("the query path must not create terminology rows");
        query.ToList().ShouldBeEmpty();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
    }

    private static SearchParameterInfo TokenParameter() =>
        new("code", "code", SearchParamType.Token, new Uri(ObservationCodeParameterUri));

    private static SearchParameterInfo QuantityParameter() =>
        new("value-quantity", "value-quantity", SearchParamType.Quantity, new Uri(ObservationValueQuantityParameterUri));
}
