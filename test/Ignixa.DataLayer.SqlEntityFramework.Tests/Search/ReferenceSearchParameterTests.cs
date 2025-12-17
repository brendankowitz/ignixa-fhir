// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Integration tests for reference search parameters.
/// Tests that reference searches with explicit resource types work correctly.
/// Regression test for: GET /Encounter?subject=Patient/searchpatient3
/// </summary>
public class ReferenceSearchParameterTests : TestBase
{
    private readonly SearchParameterQueryGenerator _queryGenerator;

    public ReferenceSearchParameterTests()
    {
        _queryGenerator = new SearchParameterQueryGenerator(
            Context,
            NullLoggerFactory.Instance.CreateLogger<SearchParameterQueryGenerator>());
    }

    [Fact]
    public async Task GivenReferenceSearchWithExplicitType_WhenSearchingEncounterBySubjectPatient_ThenReturnsCorrectEncounter()
    {
        // Arrange: Create Patient and Encounter resources
        var patient = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "searchpatient3",
            version: 1);

        var encounter = CreateResource(
            resourceTypeId: 5,  // Encounter
            resourceId: "enc-1",
            version: 1);

        // Create a reference from Encounter to Patient via "subject" parameter
        CreateReference(
            sourceSurrogateId: encounter.ResourceSurrogateId,
            sourceTypeId: 5,    // Encounter
            targetTypeId: 1,    // Patient
            targetResourceId: "searchpatient3",
            searchParamId: 6);  // subject parameter

        // Create a reference expression: subject=Patient/searchpatient3
        // This creates an AND expression with:
        // - StringEquals(ReferenceResourceType, "Patient")
        // - StringEquals(ReferenceResourceId, "searchpatient3")
        var referenceTypeExpression = new StringExpression(
            FieldName.ReferenceResourceType,
            StringOperator.Equals,
            "Patient");

        var referenceIdExpression = new StringExpression(
            FieldName.ReferenceResourceId,
            StringOperator.Equals,
            "searchpatient3");

        var combinedExpression = Expression.And(referenceTypeExpression, referenceIdExpression);

        // Act: Process the reference search for Encounter resource type
        var query = await _queryGenerator.ProcessExpressionAsync(
            resourceTypeId: 5,  // Encounter
            expr: combinedExpression,
            ct: CancellationToken.None);

        var results = await query.ToListAsync();

        // Assert: Should return only the Encounter with the matching reference
        results.ShouldHaveSingleItem();
        results.First().ShouldBe(encounter.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenReferenceSearchWithExplicitType_WhenMultipleEncountersReferenceSamePatient_ThenReturnsAll()
    {
        // Arrange: Create one Patient and multiple Encounters
        var patient = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "patient-1",
            version: 1);

        var encounter1 = CreateResource(
            resourceTypeId: 5,  // Encounter
            resourceId: "enc-1",
            version: 1);

        var encounter2 = CreateResource(
            resourceTypeId: 5,  // Encounter
            resourceId: "enc-2",
            version: 1);

        var encounter3 = CreateResource(
            resourceTypeId: 5,  // Encounter
            resourceId: "enc-3",
            version: 1);

        // Create references from both encounters to the same patient
        CreateReference(
            sourceSurrogateId: encounter1.ResourceSurrogateId,
            sourceTypeId: 5,    // Encounter
            targetTypeId: 1,    // Patient
            targetResourceId: "patient-1",
            searchParamId: 6);  // subject parameter

        CreateReference(
            sourceSurrogateId: encounter2.ResourceSurrogateId,
            sourceTypeId: 5,    // Encounter
            targetTypeId: 1,    // Patient
            targetResourceId: "patient-1",
            searchParamId: 6);  // subject parameter

        // Create a reference to a different patient (should not be included)
        var otherPatient = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "other-patient",
            version: 1);

        CreateReference(
            sourceSurrogateId: encounter3.ResourceSurrogateId,
            sourceTypeId: 5,    // Encounter
            targetTypeId: 1,    // Patient
            targetResourceId: "other-patient",
            searchParamId: 6);  // subject parameter

        // Create reference search: subject=Patient/patient-1
        var referenceTypeExpression = new StringExpression(
            FieldName.ReferenceResourceType,
            StringOperator.Equals,
            "Patient");

        var referenceIdExpression = new StringExpression(
            FieldName.ReferenceResourceId,
            StringOperator.Equals,
            "patient-1");

        var combinedExpression = Expression.And(referenceTypeExpression, referenceIdExpression);

        // Act: Process the reference search
        var query = await _queryGenerator.ProcessExpressionAsync(
            resourceTypeId: 5,  // Encounter
            expr: combinedExpression,
            ct: CancellationToken.None);

        var results = await query.ToListAsync();

        // Assert: Should return only the two encounters referencing the specified patient
        results.Count.ShouldBe(2);
        results.ShouldContain(new[] { encounter1.ResourceSurrogateId, encounter2.ResourceSurrogateId });
        results.ShouldNotContain(encounter3.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenReferenceSearchWithExplicitType_WhenNoReferencesMatch_ThenReturnsEmpty()
    {
        // Arrange: Create resources with no matching references
        var encounter = CreateResource(
            resourceTypeId: 5,  // Encounter
            resourceId: "enc-1",
            version: 1);

        var patient = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "patient-1",
            version: 1);

        // Encounter references a different patient
        CreateReference(
            sourceSurrogateId: encounter.ResourceSurrogateId,
            sourceTypeId: 5,    // Encounter
            targetTypeId: 1,    // Patient
            targetResourceId: "different-patient",
            searchParamId: 6);  // subject parameter

        // Search for references to patient-1 (which doesn't exist)
        var referenceTypeExpression = new StringExpression(
            FieldName.ReferenceResourceType,
            StringOperator.Equals,
            "Patient");

        var referenceIdExpression = new StringExpression(
            FieldName.ReferenceResourceId,
            StringOperator.Equals,
            "patient-1");

        var combinedExpression = Expression.And(referenceTypeExpression, referenceIdExpression);

        // Act: Process the reference search
        var query = await _queryGenerator.ProcessExpressionAsync(
            resourceTypeId: 5,  // Encounter
            expr: combinedExpression,
            ct: CancellationToken.None);

        var results = await query.ToListAsync();

        // Assert: Should return empty results
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenReferenceSearchWithWrongResourceType_WhenSearchingForDifferentResourceType_ThenReturnsEmpty()
    {
        // Arrange: Create Patient and Encounter
        var patient = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "patient-1",
            version: 1);

        var encounter = CreateResource(
            resourceTypeId: 5,  // Encounter
            resourceId: "enc-1",
            version: 1);

        // Create reference to Patient
        CreateReference(
            sourceSurrogateId: encounter.ResourceSurrogateId,
            sourceTypeId: 5,    // Encounter
            targetTypeId: 1,    // Patient
            targetResourceId: "patient-1",
            searchParamId: 6);  // subject parameter

        // Search for references to Organization (not Patient)
        var referenceTypeExpression = new StringExpression(
            FieldName.ReferenceResourceType,
            StringOperator.Equals,
            "Organization");  // Wrong type

        var referenceIdExpression = new StringExpression(
            FieldName.ReferenceResourceId,
            StringOperator.Equals,
            "patient-1");

        var combinedExpression = Expression.And(referenceTypeExpression, referenceIdExpression);

        // Act: Process the reference search
        var query = await _queryGenerator.ProcessExpressionAsync(
            resourceTypeId: 5,  // Encounter
            expr: combinedExpression,
            ct: CancellationToken.None);

        var results = await query.ToListAsync();

        // Assert: Should return empty results because resource type doesn't match
        results.ShouldBeEmpty();
    }
}
