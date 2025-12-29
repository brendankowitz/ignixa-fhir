// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Integration tests for _not-referenced search parameter.
/// Tests that orphaned resource searches work correctly.
/// </summary>
public class NotReferencedSearchParameterTests : TestBase
{
    private readonly SearchParameterQueryGenerator _queryGenerator;

    public NotReferencedSearchParameterTests()
    {
        _queryGenerator = new SearchParameterQueryGenerator(
            Context,
            Cache,
            NullLoggerFactory.Instance.CreateLogger<SearchParameterQueryGenerator>(),
            new CompositeSearchParameterQueryGenerator(
                Context,
                Cache,
                NullLoggerFactory.Instance.CreateLogger<CompositeSearchParameterQueryGenerator>()));
    }

    [Fact]
    public async Task GivenNotReferencedWithWildcard_WhenSearchingForOrphanedPatients_ThenReturnsUnreferencedPatients()
    {
        // Arrange: Create Patients, some referenced by Observations, some not
        var referencedPatient = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "patient-referenced",
            version: 1);

        var orphanedPatient = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "patient-orphaned",
            version: 1);

        var observation = CreateResource(
            resourceTypeId: 3,  // Observation
            resourceId: "obs-1",
            version: 1);

        CreateReference(
            sourceSurrogateId: observation.ResourceSurrogateId,
            sourceTypeId: 3,    // Observation
            targetTypeId: 1,    // Patient
            targetResourceId: "patient-referenced",
            searchParamId: 3);  // Observation.patient

        // Act: Search for patients not referenced by any resource (*:*)
        var expression = Expression.NotReferenced(null, null);
        var query = await _queryGenerator.GenerateNotReferencedQueryAsync(
            resourceTypeId: 1,  // Patient
            expression: expression,
            searchParamInfo: null!,
            ct: CancellationToken.None);

        var results = await query.ToListAsync();

        // Assert: Should return only the orphaned patient
        results.ShouldHaveSingleItem();
        results.First().ShouldBe(orphanedPatient.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenNotReferencedWithSourceType_WhenSearchingForPatientsNotReferencedByObservation_ThenReturnsCorrectPatients()
    {
        // Arrange: Create Patients referenced by different resource types
        var patientReferencedByObservation = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "patient-obs-ref",
            version: 1);

        var patientReferencedByEncounter = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "patient-enc-ref",
            version: 1);

        var orphanedPatient = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "patient-orphaned",
            version: 1);

        var observation = CreateResource(
            resourceTypeId: 3,  // Observation
            resourceId: "obs-1",
            version: 1);

        var encounter = CreateResource(
            resourceTypeId: 5,  // Encounter
            resourceId: "enc-1",
            version: 1);

        CreateReference(
            sourceSurrogateId: observation.ResourceSurrogateId,
            sourceTypeId: 3,    // Observation
            targetTypeId: 1,    // Patient
            targetResourceId: "patient-obs-ref",
            searchParamId: 3);  // Observation.patient

        CreateReference(
            sourceSurrogateId: encounter.ResourceSurrogateId,
            sourceTypeId: 5,    // Encounter
            targetTypeId: 1,    // Patient
            targetResourceId: "patient-enc-ref",
            searchParamId: 6);  // Encounter.subject

        // Act: Search for patients not referenced by Observation (Observation:*)
        var expression = Expression.NotReferenced("Observation", null);
        var query = await _queryGenerator.GenerateNotReferencedQueryAsync(
            resourceTypeId: 1,  // Patient
            expression: expression,
            searchParamInfo: null!,
            ct: CancellationToken.None);

        var results = await query.ToListAsync();

        // Assert: Should return the orphaned patient and the one referenced only by Encounter
        results.Count.ShouldBe(2);
        results.ShouldContain(orphanedPatient.ResourceSurrogateId);
        results.ShouldContain(patientReferencedByEncounter.ResourceSurrogateId);
        results.ShouldNotContain(patientReferencedByObservation.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenNotReferencedWithWildcard_WhenAllPatientsAreReferenced_ThenReturnsEmpty()
    {
        // Arrange: Create Patients all referenced by Observations
        var patient1 = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "patient-1",
            version: 1);

        var patient2 = CreateResource(
            resourceTypeId: 1,  // Patient
            resourceId: "patient-2",
            version: 1);

        var observation1 = CreateResource(
            resourceTypeId: 3,  // Observation
            resourceId: "obs-1",
            version: 1);

        var observation2 = CreateResource(
            resourceTypeId: 3,  // Observation
            resourceId: "obs-2",
            version: 1);

        CreateReference(
            sourceSurrogateId: observation1.ResourceSurrogateId,
            sourceTypeId: 3,    // Observation
            targetTypeId: 1,    // Patient
            targetResourceId: "patient-1",
            searchParamId: 3);  // Observation.patient

        CreateReference(
            sourceSurrogateId: observation2.ResourceSurrogateId,
            sourceTypeId: 3,    // Observation
            targetTypeId: 1,    // Patient
            targetResourceId: "patient-2",
            searchParamId: 3);  // Observation.patient

        // Act: Search for orphaned patients (*:*)
        var expression = Expression.NotReferenced(null, null);
        var query = await _queryGenerator.GenerateNotReferencedQueryAsync(
            resourceTypeId: 1,  // Patient
            expression: expression,
            searchParamInfo: null!,
            ct: CancellationToken.None);

        var results = await query.ToListAsync();

        // Assert: Should return empty since all patients are referenced
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenNotReferencedWithWildcard_WhenNoPatients_ThenReturnsEmpty()
    {
        // Arrange: Don't create any patients

        // Act: Search for orphaned patients
        var expression = Expression.NotReferenced(null, null);
        var query = await _queryGenerator.GenerateNotReferencedQueryAsync(
            resourceTypeId: 1,  // Patient
            expression: expression,
            searchParamInfo: null!,
            ct: CancellationToken.None);

        var results = await query.ToListAsync();

        // Assert: Should return empty
        results.ShouldBeEmpty();
    }
}
