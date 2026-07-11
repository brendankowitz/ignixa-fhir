// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Serialization.SourceNodes;
using Microsoft.IO;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Characterization tests pinning down current behavior of SearchParameterQueryGenerator's
/// resource-level parameter handling (_id, _lastUpdated, _ttl, _type) before Task 3 of the
/// SQL data layer cleanup plan extracts its duplicated BinaryOperator switches.
/// </summary>
public class SearchParameterQueryGeneratorResourceLevelTests : TestBase
{
    private readonly SearchParameterQueryGenerator _generator;
    private readonly GzipResourceCompressor _resourceCompressor = new(new RecyclableMemoryStreamManager());

    public SearchParameterQueryGeneratorResourceLevelTests()
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
    }

    /// <summary>
    /// Creates a resource with a time-encoded surrogate ID based on the provided datetime.
    /// </summary>
    private ResourceEntity CreateResourceWithTimestamp(short resourceTypeId, string resourceId, DateTimeOffset timestamp, int version = 1)
    {
        var minimalJson = @"{""resourceType"":""Resource"",""id"":""" + resourceId + @"""}";
        var compressedBytes = _resourceCompressor.SerializeAndCompress(ResourceJsonNode.Parse(minimalJson));
        var surrogateId = timestamp.ToId();

        var resource = new ResourceEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceId = resourceId,
            Version = version,
            IsHistory = false,
            IsDeleted = false,
            ResourceSurrogateId = surrogateId,
            RawResource = compressedBytes
        };

        Context.Resources.Add(resource);
        Context.SaveChanges();

        return resource;
    }

    [Fact]
    public async Task GivenIdEquality_WhenGeneratingQuery_ThenReturnsMatchingResourceOnly()
    {
        // Arrange
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateResource(resourceTypeId: 1, resourceId: "patient-2");

        var idParameter = new SearchParameterInfo("_id", "_id", SearchParamType.Token);
        var expression = new SearchParameterExpression(
            idParameter,
            new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "patient-1", false));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(patient.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenMultipleIdValues_WhenGeneratingQuery_ThenReturnsAllMatchingResources()
    {
        // Arrange
        var patient1 = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var patient2 = CreateResource(resourceTypeId: 1, resourceId: "patient-2");
        CreateResource(resourceTypeId: 1, resourceId: "patient-3");

        var idParameter = new SearchParameterInfo("_id", "_id", SearchParamType.Token);
        var expression = new SearchParameterExpression(
            idParameter,
            new MultiaryExpression(
                MultiaryOperator.Or,
                new Expression[]
                {
                    new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "patient-1", false),
                    new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "patient-2", false),
                }));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldContain(patient1.ResourceSurrogateId);
        results.ShouldContain(patient2.ResourceSurrogateId);
    }

    [Theory]
    [InlineData(BinaryOperator.Equal, 0, true)]
    [InlineData(BinaryOperator.GreaterThan, 1, false)]
    [InlineData(BinaryOperator.GreaterThanOrEqual, 0, true)]
    [InlineData(BinaryOperator.LessThan, -1, false)]
    [InlineData(BinaryOperator.LessThanOrEqual, 0, true)]
    [InlineData(BinaryOperator.NotEqual, 1, true)]
    public async Task GivenLastUpdatedComparator_WhenGeneratingQuery_ThenAppliesCorrectComparison(
        BinaryOperator op, int surrogateIdOffsetDays, bool expectMatch)
    {
        // Arrange: resource's ResourceSurrogateId encodes its creation time via IdHelper.ToId(),
        // so a resource created "now" and a target date offset by `surrogateIdOffsetDays` days
        // exercise each comparator direction using the same _lastUpdated encoding production code uses.
        var resourceDate = DateTimeOffset.UtcNow;
        var resource = CreateResourceWithTimestamp(resourceTypeId: 1, resourceId: "patient-1", timestamp: resourceDate);

        var lastUpdatedParameter = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date);
        var targetDate = resourceDate.AddDays(surrogateIdOffsetDays);
        var expression = new SearchParameterExpression(
            lastUpdatedParameter,
            new BinaryExpression(op, FieldName.DateTimeStart, null, targetDate));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert: this test's purpose is to CAPTURE current behavior, not assert a spec-correct
        // expectation. Run it against the pre-refactor code, record the actual pass/fail per
        // InlineData row in the task report, and use that recorded behavior (not `expectMatch`
        // as written) as the ground truth if it disagrees — adjust `expectMatch` values to match
        // observed pre-refactor output before committing, then this test becomes the regression
        // guard for Task 3/5.
        results.Contains(resource.ResourceSurrogateId).ShouldBe(expectMatch);
    }

    [Fact]
    public async Task GivenTtlGreaterThan_WhenGeneratingQuery_ThenReturnsResourcesWithLaterExpiry()
    {
        // Arrange
        var expiringLate = CreateResource(resourceTypeId: 1, resourceId: "patient-late");
        var expiringEarly = CreateResource(resourceTypeId: 1, resourceId: "patient-early");
        var cutoff = DateTimeOffset.UtcNow;

        Context.ResourceTtls.Add(new Ignixa.DataLayer.SqlEntityFramework.Entities.ResourceTtlEntity
        {
            ResourceTypeId = 1,
            ResourceId = expiringLate.ResourceId,
            ExpiresAt = cutoff.AddDays(1),
        });
        Context.ResourceTtls.Add(new Ignixa.DataLayer.SqlEntityFramework.Entities.ResourceTtlEntity
        {
            ResourceTypeId = 1,
            ResourceId = expiringEarly.ResourceId,
            ExpiresAt = cutoff.AddDays(-1),
        });
        await Context.SaveChangesAsync();

        var ttlParameter = new SearchParameterInfo("_ttl", "_ttl", SearchParamType.Date);
        var expression = new SearchParameterExpression(
            ttlParameter,
            new BinaryExpression(BinaryOperator.GreaterThan, FieldName.DateTimeStart, null, cutoff));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(expiringLate.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenTtlMissing_WhenGeneratingQuery_ThenReturnsOnlyResourcesWithoutTtl()
    {
        // Arrange
        var withTtl = CreateResource(resourceTypeId: 1, resourceId: "patient-with-ttl");
        var withoutTtl = CreateResource(resourceTypeId: 1, resourceId: "patient-without-ttl");

        Context.ResourceTtls.Add(new Ignixa.DataLayer.SqlEntityFramework.Entities.ResourceTtlEntity
        {
            ResourceTypeId = 1,
            ResourceId = withTtl.ResourceId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        });
        await Context.SaveChangesAsync();

        var ttlParameter = new SearchParameterInfo("_ttl", "_ttl", SearchParamType.Date);
        var expression = new SearchParameterExpression(
            ttlParameter,
            new MissingSearchParameterExpression(ttlParameter, isMissing: true));

        // Act
        var query = await _generator.GenerateQueryAsync(resourceTypeId: 1, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(withoutTtl.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenTypeEquality_WhenGeneratingQuery_ThenReturnsOnlyMatchingType()
    {
        // Arrange
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateResource(resourceTypeId: 2, resourceId: "org-1");

        var typeParameter = new SearchParameterInfo("_type", "_type", SearchParamType.Token);
        var expression = new SearchParameterExpression(
            typeParameter,
            new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "Patient", false));

        // Act: system-wide search (resourceTypeId: null) so _type is the only type filter
        var query = await _generator.GenerateQueryAsync(resourceTypeId: null, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(patient.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenTypeMultipleValues_WhenGeneratingQuery_ThenReturnsAllMatchingTypes()
    {
        // Arrange: system-wide search with _type filtering for multiple resource types
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var org = CreateResource(resourceTypeId: 2, resourceId: "org-1");
        CreateResource(resourceTypeId: 3, resourceId: "obs-1");

        var typeParameter = new SearchParameterInfo("_type", "_type", SearchParamType.Token);
        var expression = new SearchParameterExpression(
            typeParameter,
            new MultiaryExpression(
                MultiaryOperator.Or,
                new Expression[]
                {
                    new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "Patient", false),
                    new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "Organization", false),
                }));

        // Act: system-wide search (resourceTypeId: null) with multi-value _type filter
        var query = await _generator.GenerateQueryAsync(resourceTypeId: null, expression, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldContain(patient.ResourceSurrogateId);
        results.ShouldContain(org.ResourceSurrogateId);
    }
}
