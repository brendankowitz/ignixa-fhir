// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Integration tests for ChainedExpressionProcessor.
/// Tests forward chains (Patient?organization.name=Acme) and reverse chains (_has).
/// </summary>
public class ChainedExpressionProcessorTests : TestBase
{
    private readonly ChainedExpressionProcessor _processor;
    private readonly SearchParameterQueryGenerator _parameterQueryGenerator;

    public ChainedExpressionProcessorTests()
    {
        _parameterQueryGenerator = new SearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<SearchParameterQueryGenerator>());

        _processor = new ChainedExpressionProcessor(
            Context,
            Cache,
            _parameterQueryGenerator,
            LoggerFactory.CreateLogger<ChainedExpressionProcessor>());
    }

    #region Forward Chain Tests

    [Fact]
    public async Task GivenForwardChain_WhenPatientReferencesMatchingOrganization_ThenReturnsPatient()
    {
        // Arrange: Create Organization with name "Acme"
        var org = CreateResource(resourceTypeId: 2, resourceId: "org-1");
        CreateStringSearchParam(org.ResourceSurrogateId, resourceTypeId: 2, searchParamId: 5, text: "Acme");

        // Create Patient that references the Organization
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);

        // Create chain expression: Patient?organization.name=Acme
        var targetExpression = new SearchParameterExpression(
            new SearchParameterInfo("name", SearchParamType.String),
            new StringExpression(StringOperator.Equals, "Acme", false));

        var referenceSearchParam = new SearchParameterInfo("organization", SearchParamType.Reference)
        {
            TargetResourceTypes = new[] { "Organization" }
        };

        var chainedExpression = new ChainedExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: referenceSearchParam,
            targetResourceTypes: new[] { "Organization" },
            reversed: false,
            expression: targetExpression);

        // Act
        var result = await _processor.ProcessChainAsync(resourceTypeId: 1, chainedExpression, CancellationToken.None);
        var surrogateIds = await result.ToListAsync();

        // Assert
        surrogateIds.Should().ContainSingle();
        surrogateIds.First().Should().Be(patient.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenForwardChain_WhenNoMatchingTarget_ThenReturnsEmpty()
    {
        // Arrange: Create Organization with name "Hospital" (not matching)
        var org = CreateResource(resourceTypeId: 2, resourceId: "org-1");
        CreateStringSearchParam(org.ResourceSurrogateId, resourceTypeId: 2, searchParamId: 5, text: "Hospital");

        // Create Patient that references the Organization
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);

        // Create chain expression looking for "Acme"
        var targetExpression = new SearchParameterExpression(
            new SearchParameterInfo("name", SearchParamType.String),
            new StringExpression(StringOperator.Equals, "Acme", false));

        var referenceSearchParam = new SearchParameterInfo("organization", SearchParamType.Reference)
        {
            TargetResourceTypes = new[] { "Organization" }
        };

        var chainedExpression = new ChainedExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: referenceSearchParam,
            targetResourceTypes: new[] { "Organization" },
            reversed: false,
            expression: targetExpression);

        // Act
        var result = await _processor.ProcessChainAsync(resourceTypeId: 1, chainedExpression, CancellationToken.None);
        var surrogateIds = await result.ToListAsync();

        // Assert
        surrogateIds.Should().BeEmpty();
    }

    #endregion

    #region Reverse Chain Tests

    [Fact]
    public async Task GivenReverseChain_WhenObservationReferencesPatient_ThenReturnsPatient()
    {
        // Arrange: Create Patient
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");

        // Create Observation that references Patient with code "12345"
        var observation = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        CreateReference(observation.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);
        CreateStringSearchParam(observation.ResourceSurrogateId, resourceTypeId: 3, searchParamId: 4, text: "12345");

        // Create reverse chain: Patient?_has:Observation:patient:code=12345
        var targetExpression = new SearchParameterExpression(
            new SearchParameterInfo("code", SearchParamType.String),
            new StringExpression(StringOperator.Equals, "12345", false));

        var referenceSearchParam = new SearchParameterInfo("patient", SearchParamType.Reference)
        {
            TargetResourceTypes = new[] { "Patient" }
        };

        var chainedExpression = new ChainedExpression(
            resourceTypes: new[] { "Observation" },
            referenceSearchParameter: referenceSearchParam,
            targetResourceTypes: new[] { "Observation" },
            reversed: true,
            expression: targetExpression);

        // Act
        var result = await _processor.ProcessChainAsync(resourceTypeId: 1, chainedExpression, CancellationToken.None);
        var surrogateIds = await result.ToListAsync();

        // Assert
        surrogateIds.Should().ContainSingle();
        surrogateIds.First().Should().Be(patient.ResourceSurrogateId);
    }

    [Fact]
    public async Task GivenReverseChain_WhenNoMatchingReferencer_ThenReturnsEmpty()
    {
        // Arrange: Create Patient
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");

        // Create Observation with code "99999" (not matching)
        var observation = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        CreateReference(observation.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);
        CreateStringSearchParam(observation.ResourceSurrogateId, resourceTypeId: 3, searchParamId: 4, text: "99999");

        // Create reverse chain looking for code "12345"
        var targetExpression = new SearchParameterExpression(
            new SearchParameterInfo("code", SearchParamType.String),
            new StringExpression(StringOperator.Equals, "12345", false));

        var referenceSearchParam = new SearchParameterInfo("patient", SearchParamType.Reference)
        {
            TargetResourceTypes = new[] { "Patient" }
        };

        var chainedExpression = new ChainedExpression(
            resourceTypes: new[] { "Observation" },
            referenceSearchParameter: referenceSearchParam,
            targetResourceTypes: new[] { "Observation" },
            reversed: true,
            expression: targetExpression);

        // Act
        var result = await _processor.ProcessChainAsync(resourceTypeId: 1, chainedExpression, CancellationToken.None);
        var surrogateIds = await result.ToListAsync();

        // Assert
        surrogateIds.Should().BeEmpty();
    }

    #endregion
}
