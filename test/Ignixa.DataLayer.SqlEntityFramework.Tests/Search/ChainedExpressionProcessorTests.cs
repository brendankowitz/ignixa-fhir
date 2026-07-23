// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Integration tests for ChainedExpressionProcessor.
/// Tests forward chains (Patient?organization._id=org-1) and reverse chains (_has).
/// </summary>
/// <remarks>
/// The chain leaf filters on _id rather than on a string search parameter. The string leaf
/// (GenerateStringQueryAsync) is built on EF.Functions.Collate, which the InMemory provider cannot
/// evaluate at all -- it throws "The 'Collate' method is not supported because the query has switched
/// to client-evaluation" before any chain logic runs. _id resolves against the Resource table with
/// plain LINQ, so the join being tested here is the same one, exercised end to end.
/// </remarks>
public class ChainedExpressionProcessorTests : TestBase
{
    private const short PatientResourceTypeId = 1;

    private readonly ChainedExpressionProcessor _processor;

    public ChainedExpressionProcessorTests()
    {
        var compositeQueryGenerator = new CompositeSearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());

        var parameterQueryGenerator = new SearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<SearchParameterQueryGenerator>(),
            compositeQueryGenerator);

        _processor = new ChainedExpressionProcessor(
            Context,
            Cache,
            parameterQueryGenerator,
            LoggerFactory.CreateLogger<ChainedExpressionProcessor>());
    }

    [Fact]
    public async Task GivenForwardChain_WhenPatientReferencesMatchingOrganization_ThenReturnsPatient()
    {
        // Arrange: Create Organization org-1
        CreateResource(resourceTypeId: 2, resourceId: "org-1");

        // Create Patient that references the Organization
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);

        // Create chain expression: Patient?organization._id=org-1
        var chainedExpression = CreateForwardChain("org-1");

        // Act
        var result = await _processor.ProcessChainAsync(PatientResourceTypeId, chainedExpression, CancellationToken.None);
        var surrogateIds = await result.ToListAsync();

        // Assert
        surrogateIds.ShouldHaveSingleItem();
        surrogateIds[0].ShouldBe(patient.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenForwardChain_WhenNoMatchingTarget_ThenReturnsEmpty()
    {
        // Arrange: Create Organization org-1 (the chain will look for org-2)
        CreateResource(resourceTypeId: 2, resourceId: "org-1");
        CreateResource(resourceTypeId: 2, resourceId: "org-2");

        // Create Patient that references org-1 only
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);

        // Create chain expression looking for org-2
        var chainedExpression = CreateForwardChain("org-2");

        // Act
        var result = await _processor.ProcessChainAsync(PatientResourceTypeId, chainedExpression, CancellationToken.None);
        var surrogateIds = await result.ToListAsync();

        // Assert
        surrogateIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenReverseChain_WhenObservationReferencesPatient_ThenReturnsPatient()
    {
        // Arrange: Create Patient
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");

        // Create Observation obs-1 that references Patient
        var observation = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        CreateReference(observation.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);

        // Create reverse chain: Patient?_has:Observation:patient:_id=obs-1
        var chainedExpression = CreateReverseChain("obs-1");

        // Act
        var result = await _processor.ProcessChainAsync(PatientResourceTypeId, chainedExpression, CancellationToken.None);
        var surrogateIds = await result.ToListAsync();

        // Assert
        surrogateIds.ShouldHaveSingleItem();
        surrogateIds[0].ShouldBe(patient.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenReverseChain_WhenNoMatchingReferencer_ThenReturnsEmpty()
    {
        // Arrange: Create Patient
        CreateResource(resourceTypeId: 1, resourceId: "patient-1");

        // Create Observation obs-1 referencing the Patient, plus an unrelated obs-2
        var observation = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        CreateReference(observation.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);
        CreateResource(resourceTypeId: 3, resourceId: "obs-2");

        // Create reverse chain looking for obs-2, which does not reference the Patient
        var chainedExpression = CreateReverseChain("obs-2");

        // Act
        var result = await _processor.ProcessChainAsync(PatientResourceTypeId, chainedExpression, CancellationToken.None);
        var surrogateIds = await result.ToListAsync();

        // Assert
        surrogateIds.ShouldBeEmpty();
    }

    private static ChainedExpression CreateForwardChain(string organizationId)
    {
        var targetExpression = CreateResourceIdExpression(organizationId);

        var referenceSearchParam = new SearchParameterInfo(
            name: "organization",
            code: "organization",
            searchParamType: SearchParamType.Reference,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: new[] { "Organization" });

        return new ChainedExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: referenceSearchParam,
            targetResourceTypes: new[] { "Organization" },
            reversed: false,
            expression: targetExpression);
    }

    private static ChainedExpression CreateReverseChain(string observationId)
    {
        var targetExpression = CreateResourceIdExpression(observationId);

        var referenceSearchParam = new SearchParameterInfo(
            name: "patient",
            code: "patient",
            searchParamType: SearchParamType.Reference,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-patient"),
            targetResourceTypes: new[] { "Patient" });

        return new ChainedExpression(
            resourceTypes: new[] { "Observation" },
            referenceSearchParameter: referenceSearchParam,
            targetResourceTypes: new[] { "Observation" },
            reversed: true,
            expression: targetExpression);
    }

    private static SearchParameterExpression CreateResourceIdExpression(string resourceId)
    {
        return new SearchParameterExpression(
            new SearchParameterInfo("_id", "_id", SearchParamType.Token),
            new StringExpression(StringOperator.Equals, FieldName.TokenCode, componentIndex: null, resourceId, ignoreCase: false));
    }
}
