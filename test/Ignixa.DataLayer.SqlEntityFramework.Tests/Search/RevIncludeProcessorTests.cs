// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.SourceNodeSerialization;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Integration tests for RevIncludeProcessor.
/// Tests _revinclude functionality for fetching resources that reference the main results.
/// </summary>
public class RevIncludeProcessorTests : TestBase
{
    private readonly RevIncludeProcessor _processor;

    public RevIncludeProcessorTests()
    {
        var compressor = new GzipResourceCompressor();
        _processor = new RevIncludeProcessor(
            Context,
            Cache,
            compressor,
            NullLoggerFactory.Instance.CreateLogger<RevIncludeProcessor>());
    }

    [Fact]
    public async Task GivenRevInclude_WhenObservationsReferencePatient_ThenReturnsObservations()
    {
        // Arrange: Create Patient and Observation
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var observation = CreateResource(resourceTypeId: 3, resourceId: "obs-1");

        // Observation references Patient
        CreateReference(observation.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);

        // Mock repository to return Observation when requested
        var obsWrapper = new ResourceWrapper(
            ResourceType: "Observation",
            ResourceId: "obs-1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: Substitute.For<ISourceNode>(),
            Request: new ResourceRequest());

        MockRepository.GetAsync(
            Arg.Is<ResourceKey>(k => k.ResourceType == "Observation" && k.Id == "obs-1"),
            Arg.Any<CancellationToken>())
            .Returns(obsWrapper);

        // Create main results (Patient)
        var mainResults = new List<ResourceWrapper>
        {
            new ResourceWrapper(
                ResourceType: "Patient",
                ResourceId: "patient-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                Resource: Substitute.For<ISourceNode>(),
                Request: new ResourceRequest())
        };

        // Create revinclude expression: _revinclude=Observation:patient
        var revIncludeExpression = new IncludeExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: new SearchParameterInfo("patient", SearchParamType.Reference)
            {
                TargetResourceTypes = new[] { "Patient" }
            },
            sourceResourceType: "Observation",
            targetResourceType: "Patient",
            referencedTypes: new[] { "Patient" },
            wildCard: false,
            reversed: true,
            iterate: false);

        // Act
        var result = await _processor.ProcessRevIncludesAsync(mainResults, new[] { revIncludeExpression }, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result.First().ResourceType.Should().Be("Observation");
        result.First().ResourceId.Should().Be("obs-1");
    }

    [Fact]
    public async Task GivenRevInclude_WhenMultipleObservationsReferencePatient_ThenReturnsAll()
    {
        // Arrange: Create Patient and multiple Observations
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var obs1 = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        var obs2 = CreateResource(resourceTypeId: 3, resourceId: "obs-2");

        // Both Observations reference Patient
        CreateReference(obs1.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);
        CreateReference(obs2.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);

        // Mock repository responses
        MockRepository.GetAsync(
            Arg.Is<ResourceKey>(k => k.ResourceType == "Observation" && k.Id == "obs-1"),
            Arg.Any<CancellationToken>())
            .Returns(new ResourceWrapper(
                ResourceType: "Observation",
                ResourceId: "obs-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                Resource: Substitute.For<ISourceNode>(),
                Request: new ResourceRequest()));

        MockRepository.GetAsync(
            Arg.Is<ResourceKey>(k => k.ResourceType == "Observation" && k.Id == "obs-2"),
            Arg.Any<CancellationToken>())
            .Returns(new ResourceWrapper(
                ResourceType: "Observation",
                ResourceId: "obs-2",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                Resource: Substitute.For<ISourceNode>(),
                Request: new ResourceRequest()));

        var mainResults = new List<ResourceWrapper>
        {
            new ResourceWrapper(
                ResourceType: "Patient",
                ResourceId: "patient-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                Resource: Substitute.For<ISourceNode>(),
                Request: new ResourceRequest())
        };

        var revIncludeExpression = new IncludeExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: new SearchParameterInfo("patient", SearchParamType.Reference)
            {
                TargetResourceTypes = new[] { "Patient" }
            },
            sourceResourceType: "Observation",
            targetResourceType: "Patient",
            referencedTypes: new[] { "Patient" },
            wildCard: false,
            reversed: true,
            iterate: false);

        // Act
        var result = await _processor.ProcessRevIncludesAsync(mainResults, new[] { revIncludeExpression }, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.ResourceId == "obs-1");
        result.Should().Contain(r => r.ResourceId == "obs-2");
    }

    [Fact]
    public async Task GivenRevInclude_WhenNoReferencingResources_ThenReturnsEmpty()
    {
        // Arrange: Create Patient with no Observations
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");

        var mainResults = new List<ResourceWrapper>
        {
            new ResourceWrapper(
                ResourceType: "Patient",
                ResourceId: "patient-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                Resource: Substitute.For<ISourceNode>(),
                Request: new ResourceRequest())
        };

        var revIncludeExpression = new IncludeExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: new SearchParameterInfo("patient", SearchParamType.Reference)
            {
                TargetResourceTypes = new[] { "Patient" }
            },
            sourceResourceType: "Observation",
            targetResourceType: "Patient",
            referencedTypes: new[] { "Patient" },
            wildCard: false,
            reversed: true,
            iterate: false);

        // Act
        var result = await _processor.ProcessRevIncludesAsync(mainResults, new[] { revIncludeExpression }, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
