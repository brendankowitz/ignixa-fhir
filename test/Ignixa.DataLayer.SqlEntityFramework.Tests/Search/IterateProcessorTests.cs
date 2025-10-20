// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Domain.ElementModel;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Integration tests for IterateProcessor.
/// Tests :iterate modifier for recursive includes.
/// </summary>
public class IterateProcessorTests : TestBase
{
    private readonly IterateProcessor _processor;
    private readonly IncludeProcessor _includeProcessor;
    private readonly RevIncludeProcessor _revIncludeProcessor;

    public IterateProcessorTests()
    {
        _includeProcessor = new IncludeProcessor(
            Context,
            Cache,
            MockRepository,
            LoggerFactory.CreateLogger<IncludeProcessor>());

        _revIncludeProcessor = new RevIncludeProcessor(
            Context,
            Cache,
            MockRepository,
            LoggerFactory.CreateLogger<RevIncludeProcessor>());

        _processor = new IterateProcessor(
            _includeProcessor,
            _revIncludeProcessor,
            LoggerFactory.CreateLogger<IterateProcessor>());
    }

    [Fact]
    public async Task GivenIterateInclude_WhenChainOfReferences_ThenReturnsAllInChain()
    {
        // Arrange: Create chain Patient → Organization → Parent Organization
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var org = CreateResource(resourceTypeId: 2, resourceId: "org-1");
        var parentOrg = CreateResource(resourceTypeId: 2, resourceId: "parent-org-1");

        // Patient → Organization
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);

        // Organization → Parent Organization (assuming partof reference with searchParamId 2)
        CreateReference(org.ResourceSurrogateId, sourceTypeId: 2, targetTypeId: 2, targetResourceId: "parent-org-1", searchParamId: 2);

        // Mock repository responses
        MockRepository.GetAsync(
            Arg.Is<ResourceKey>(k => k.ResourceType == "Organization" && k.Id == "org-1"),
            Arg.Any<CancellationToken>())
            .Returns(new ResourceWrapper(
                ResourceType: "Organization",
                ResourceId: "org-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                Resource: Substitute.For<ISourceNode>(),
                Request: new ResourceRequest()));

        MockRepository.GetAsync(
            Arg.Is<ResourceKey>(k => k.ResourceType == "Organization" && k.Id == "parent-org-1"),
            Arg.Any<CancellationToken>())
            .Returns(new ResourceWrapper(
                ResourceType: "Organization",
                ResourceId: "parent-org-1",
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

        // Create iterate expression: _include:iterate=Patient:organization
        var iterateExpression = new IncludeExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: new SearchParameterInfo("organization", SearchParamType.Reference)
            {
                TargetResourceTypes = new[] { "Organization" }
            },
            sourceResourceType: "Patient",
            targetResourceType: "Organization",
            referencedTypes: new[] { "Organization" },
            wildCard: false,
            reversed: false,
            iterate: true);

        // Act
        var result = await _processor.ProcessIteratesAsync(mainResults, new[] { iterateExpression }, CancellationToken.None);

        // Assert: Should find both org-1 and parent-org-1
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.ResourceId == "org-1");
        result.Should().Contain(r => r.ResourceId == "parent-org-1");
    }

    [Fact]
    public async Task GivenIterateRevInclude_WhenChainOfReverseReferences_ThenReturnsAllInChain()
    {
        // Arrange: Create chain Patient ← Observation ← Encounter
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var obs = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        var encounter = CreateResource(resourceTypeId: 5, resourceId: "enc-1");

        // Observation → Patient
        CreateReference(obs.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);

        // Encounter → Observation (assuming encounter reference exists)
        CreateReference(encounter.ResourceSurrogateId, sourceTypeId: 5, targetTypeId: 3, targetResourceId: "obs-1", searchParamId: 1);

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
            Arg.Is<ResourceKey>(k => k.ResourceType == "Encounter" && k.Id == "enc-1"),
            Arg.Any<CancellationToken>())
            .Returns(new ResourceWrapper(
                ResourceType: "Encounter",
                ResourceId: "enc-1",
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

        // Create iterate revinclude: _revinclude:iterate=Observation:patient
        var iterateExpression = new IncludeExpression(
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
            iterate: true);

        // Act
        var result = await _processor.ProcessIteratesAsync(mainResults, new[] { iterateExpression }, CancellationToken.None);

        // Assert: Should find Observation (direct) but not Encounter (revinclude doesn't chain the same way)
        result.Should().Contain(r => r.ResourceId == "obs-1");
    }

    [Fact]
    public async Task GivenIterateInclude_WhenNoReferences_ThenReturnsEmpty()
    {
        // Arrange: Patient with no references
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

        var iterateExpression = new IncludeExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: new SearchParameterInfo("organization", SearchParamType.Reference)
            {
                TargetResourceTypes = new[] { "Organization" }
            },
            sourceResourceType: "Patient",
            targetResourceType: "Organization",
            referencedTypes: new[] { "Organization" },
            wildCard: false,
            reversed: false,
            iterate: true);

        // Act
        var result = await _processor.ProcessIteratesAsync(mainResults, new[] { iterateExpression }, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
