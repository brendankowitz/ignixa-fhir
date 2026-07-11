// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;

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
        var memoryStreamManager = new RecyclableMemoryStreamManager();
        var compressor = new GzipResourceCompressor(memoryStreamManager);

        _includeProcessor = new IncludeProcessor(
            Context,
            Cache,
            compressor,
            NullLogger<IncludeProcessor>.Instance);

        _revIncludeProcessor = new RevIncludeProcessor(
            Context,
            Cache,
            compressor,
            NullLogger<RevIncludeProcessor>.Instance);

        _processor = new IterateProcessor(
            _includeProcessor,
            _revIncludeProcessor,
            NullLogger<IterateProcessor>.Instance);
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
            .Returns(new SearchEntryResult(
                ResourceType: "Organization",
                ResourceId: "org-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: ReadOnlyMemory<byte>.Empty));

        MockRepository.GetAsync(
            Arg.Is<ResourceKey>(k => k.ResourceType == "Organization" && k.Id == "parent-org-1"),
            Arg.Any<CancellationToken>())
            .Returns(new SearchEntryResult(
                ResourceType: "Organization",
                ResourceId: "parent-org-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: ReadOnlyMemory<byte>.Empty));

        var mainResults = new List<SearchEntryResult>
        {
            new SearchEntryResult(
                ResourceType: "Patient",
                ResourceId: "patient-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: ReadOnlyMemory<byte>.Empty)
        };

        // Create iterate expression: _include:iterate=Patient:organization
        var iterateExpression = new IncludeExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: new SearchParameterInfo("organization", "organization", SearchParamType.Reference, targetResourceTypes: new[] { "Organization" }),
            sourceResourceType: "Patient",
            targetResourceType: "Organization",
            referencedTypes: new[] { "Organization" },
            wildCard: false,
            reversed: false,
            iterate: true);

        // Act
        var result = await _processor.ProcessIteratesAsync(mainResults, new[] { iterateExpression }, CancellationToken.None);

        // Assert: Should find both org-1 and parent-org-1
        result.Count.ShouldBe(2);
        result.ShouldContain(r => r.ResourceId == "org-1");
        result.ShouldContain(r => r.ResourceId == "parent-org-1");
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
            .Returns(new SearchEntryResult(
                ResourceType: "Observation",
                ResourceId: "obs-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: ReadOnlyMemory<byte>.Empty));

        MockRepository.GetAsync(
            Arg.Is<ResourceKey>(k => k.ResourceType == "Encounter" && k.Id == "enc-1"),
            Arg.Any<CancellationToken>())
            .Returns(new SearchEntryResult(
                ResourceType: "Encounter",
                ResourceId: "enc-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: ReadOnlyMemory<byte>.Empty));

        var mainResults = new List<SearchEntryResult>
        {
            new SearchEntryResult(
                ResourceType: "Patient",
                ResourceId: "patient-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: ReadOnlyMemory<byte>.Empty)
        };

        // Create iterate revinclude: _revinclude:iterate=Observation:patient
        var iterateExpression = new IncludeExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: new SearchParameterInfo("patient", "patient", SearchParamType.Reference, targetResourceTypes: new[] { "Patient" }),
            sourceResourceType: "Observation",
            targetResourceType: "Patient",
            referencedTypes: new[] { "Patient" },
            wildCard: false,
            reversed: true,
            iterate: true);

        // Act
        var result = await _processor.ProcessIteratesAsync(mainResults, new[] { iterateExpression }, CancellationToken.None);

        // Assert: Should find Observation (direct) but not Encounter (revinclude doesn't chain the same way)
        result.ShouldContain(r => r.ResourceId == "obs-1");
    }

    [Fact]
    public async Task GivenIterateInclude_WhenNoReferences_ThenReturnsEmpty()
    {
        // Arrange: Patient with no references
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");

        var mainResults = new List<SearchEntryResult>
        {
            new SearchEntryResult(
                ResourceType: "Patient",
                ResourceId: "patient-1",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: ReadOnlyMemory<byte>.Empty)
        };

        var iterateExpression = new IncludeExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: new SearchParameterInfo("organization", "organization", SearchParamType.Reference, targetResourceTypes: new[] { "Organization" }),
            sourceResourceType: "Patient",
            targetResourceType: "Organization",
            referencedTypes: new[] { "Organization" },
            wildCard: false,
            reversed: false,
            iterate: true);

        // Act
        var result = await _processor.ProcessIteratesAsync(mainResults, new[] { iterateExpression }, CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }
}
