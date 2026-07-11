// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Characterization tests pinning down current behavior of CompositeSearchParameterQueryGenerator
/// across its five supported composite shapes, before Task 4 of the SQL data layer cleanup plan
/// extracts its token/system encoding logic into a shared helper.
/// </summary>
public class CompositeSearchParameterQueryGeneratorTests : TestBase
{
    private readonly CompositeSearchParameterQueryGenerator _generator;

    public CompositeSearchParameterQueryGeneratorTests()
    {
        _generator = new CompositeSearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());
    }

    [Fact]
    public async Task GivenTokenTokenComposite_WhenBothComponentsMatch_ThenReturnsResource()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        const short searchParamId = 100;

        Context.TokenTokenCompositeSearchParams.Add(new TokenTokenCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            Code1 = "8480-6",
            SystemId1 = null,
            Code2 = "final",
            SystemId2 = null,
        });
        await Context.SaveChangesAsync();

        var component0 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "8480-6", false);
        var component1 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "final", false);

        // Act
        var query = await _generator.GenerateTokenTokenQueryAsync(resourceTypeId: 3, searchParamId, component0, component1, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenTokenQuantityComposite_WhenValueInRange_ThenReturnsResource()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        const short searchParamId = 101;

        Context.TokenQuantityCompositeSearchParams.Add(new TokenQuantityCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            Code1 = "8462-4",
            SystemId1 = null,
            LowValue = 80m,
            HighValue = 80m,
        });
        await Context.SaveChangesAsync();

        var component0 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "8462-4", false);
        var component1 = new MultiaryExpression(
            MultiaryOperator.And,
            new Expression[]
            {
                new BinaryExpression(BinaryOperator.GreaterThanOrEqual, FieldName.Quantity, null, 80m),
                new BinaryExpression(BinaryOperator.LessThanOrEqual, FieldName.Quantity, null, 80m),
            });

        // Act
        var query = await _generator.GenerateTokenQuantityQueryAsync(resourceTypeId: 3, searchParamId, component0, component1, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenTokenDateTimeComposite_WhenDateMatches_ThenReturnsResource()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        const short searchParamId = 102;
        var targetDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        Context.TokenDateTimeCompositeSearchParams.Add(new TokenDateTimeCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            Code1 = "status",
            SystemId1 = null,
            StartDateTime2 = targetDate,
            EndDateTime2 = targetDate,
        });
        await Context.SaveChangesAsync();

        var component0 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "status", false);
        var component1 = new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeStart, null, targetDate);

        // Act
        var query = await _generator.GenerateTokenDateTimeQueryAsync(resourceTypeId: 3, searchParamId, component0, component1, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenTokenStringComposite_WhenStringPrefixMatches_ThenReturnsResource()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        const short searchParamId = 103;

        Context.TokenStringCompositeSearchParams.Add(new TokenStringCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            Code1 = "component-code",
            SystemId1 = null,
            Text2 = "SMITH",
        });
        await Context.SaveChangesAsync();

        var component0 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "component-code", false);
        var component1 = new StringExpression(StringOperator.Equals, FieldName.String, null, "Smith", false);

        // Act
        var query = await _generator.GenerateTokenStringQueryAsync(resourceTypeId: 3, searchParamId, component0, component1, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenReferenceTokenComposite_WhenComponentsInExpectedOrder_ThenReturnsResource()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "docref-1");
        const short searchParamId = 104;

        Context.ReferenceTokenCompositeSearchParams.Add(new ReferenceTokenCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            ReferenceResourceId1 = "practitioner-1",
            Code2 = "author",
            SystemId2 = null,
        });
        await Context.SaveChangesAsync();

        var referenceComponent = new StringExpression(StringOperator.Equals, FieldName.ReferenceResourceId, null, "practitioner-1", false);
        var tokenComponent = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "author", false);

        // Act: component0 = reference, component1 = token (expected order)
        var query = await _generator.GenerateReferenceTokenQueryAsync(resourceTypeId: 3, searchParamId, referenceComponent, tokenComponent, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenReferenceTokenComposite_WhenComponentsSwapped_ThenStillReturnsResource()
    {
        // Arrange: reproduces the DocumentReference "relationship" case
        // (CompositeSearchParameterQueryGenerator.cs:318-346) where FHIR's spec-defined component
        // order is inconsistent and the generator must detect the swap at runtime.
        var resource = CreateResource(resourceTypeId: 3, resourceId: "docref-1");
        const short searchParamId = 105;

        Context.ReferenceTokenCompositeSearchParams.Add(new ReferenceTokenCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            ReferenceResourceId1 = "docref-2",
            Code2 = "replaces",
            SystemId2 = null,
        });
        await Context.SaveChangesAsync();

        var tokenComponent = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "replaces", false);
        var referenceComponent = new StringExpression(StringOperator.Equals, FieldName.ReferenceResourceId, null, "docref-2", false);

        // Act: component0 = token, component1 = reference (swapped order)
        var query = await _generator.GenerateReferenceTokenQueryAsync(resourceTypeId: 3, searchParamId, tokenComponent, referenceComponent, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert
        results.ShouldHaveSingleItem();
        results[0].ShouldBe(resource.ResourceSurrogateId);
    }
}
