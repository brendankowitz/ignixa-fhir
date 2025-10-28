// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.SourceNodeSerialization;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Integration tests for IncludeProcessor.
/// Tests _include functionality for fetching referenced resources.
/// </summary>
public class IncludeProcessorTests : TestBase
{
    private readonly IncludeProcessor _processor;

    public IncludeProcessorTests()
    {
        var memoryStreamManager = new RecyclableMemoryStreamManager();
        var compressor = new GzipResourceCompressor(memoryStreamManager);
        _processor = new IncludeProcessor(
            Context,
            Cache,
            compressor,
            NullLoggerFactory.Instance.CreateLogger<IncludeProcessor>());
    }

    [Fact]
    public async Task GivenInclude_WhenPatientReferencesOrganization_ThenReturnsOrganization()
    {
        // Arrange: Create Patient and Organization
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var organization = CreateResource(resourceTypeId: 2, resourceId: "org-1");

        // Patient references Organization
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);

        // Mock repository to return Organization when requested
        var orgWrapper = new ResourceWrapper(
            ResourceType: "Organization",
            ResourceId: "org-1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: Substitute.For<ISourceNode>(),
            Request: new ResourceRequest());

        MockRepository.GetAsync(
            Arg.Is<ResourceKey>(k => k.ResourceType == "Organization" && k.Id == "org-1"),
            Arg.Any<CancellationToken>())
            .Returns(orgWrapper);

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

        // Create include expression: _include=Patient:organization
        var includeExpression = new IncludeExpression(
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
            iterate: false);

        // Act
        var result = await _processor.ProcessIncludesAsync(mainResults, new[] { includeExpression }, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result.First().ResourceType.Should().Be("Organization");
        result.First().ResourceId.Should().Be("org-1");
    }

    [Fact]
    public async Task GivenWildcardInclude_WhenPatientHasMultipleReferences_ThenReturnsAllReferenced()
    {
        // Arrange: Create Patient, Organization, and Practitioner
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var organization = CreateResource(resourceTypeId: 2, resourceId: "org-1");
        var practitioner = CreateResource(resourceTypeId: 4, resourceId: "pract-1");

        // Patient references both
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 4, targetResourceId: "pract-1", searchParamId: 1);

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
            Arg.Is<ResourceKey>(k => k.ResourceType == "Practitioner" && k.Id == "pract-1"),
            Arg.Any<CancellationToken>())
            .Returns(new ResourceWrapper(
                ResourceType: "Practitioner",
                ResourceId: "pract-1",
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

        // Create wildcard include: _include=Patient:*
        var includeExpression = new IncludeExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: null!,
            sourceResourceType: "Patient",
            targetResourceType: null!,
            referencedTypes: new[] { "Organization", "Practitioner" },
            wildCard: true,
            reversed: false,
            iterate: false);

        // Act
        var result = await _processor.ProcessIncludesAsync(mainResults, new[] { includeExpression }, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.ResourceType == "Organization" && r.ResourceId == "org-1");
        result.Should().Contain(r => r.ResourceType == "Practitioner" && r.ResourceId == "pract-1");
    }

    [Fact]
    public async Task GivenInclude_WhenNoDuplicates_ThenReturnsSingleResource()
    {
        // Arrange: Two Patients reference same Organization
        var patient1 = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var patient2 = CreateResource(resourceTypeId: 1, resourceId: "patient-2");
        var organization = CreateResource(resourceTypeId: 2, resourceId: "org-1");

        CreateReference(patient1.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);
        CreateReference(patient2.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);

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

        var mainResults = new List<ResourceWrapper>
        {
            new ResourceWrapper(
                ResourceType: "Patient",
                ResourceId: "patient-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                Resource: Substitute.For<ISourceNode>(),
                Request: new ResourceRequest()),
            new ResourceWrapper(
                ResourceType: "Patient",
                ResourceId: "patient-2",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                Resource: Substitute.For<ISourceNode>(),
                Request: new ResourceRequest())
        };

        var includeExpression = new IncludeExpression(
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
            iterate: false);

        // Act
        var result = await _processor.ProcessIncludesAsync(mainResults, new[] { includeExpression }, CancellationToken.None);

        // Assert: Should only return Organization once (deduplication)
        result.Should().ContainSingle();
        result.First().ResourceId.Should().Be("org-1");
    }
}
