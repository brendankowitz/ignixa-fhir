// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Integration tests for IterateProcessor.
/// Tests :iterate modifier for recursive includes.
/// </summary>
public class IterateProcessorTests : TestBase
{
    private const short OrganizationPartOfSearchParamId = 7;

    private readonly IterateProcessor _processor;

    public IterateProcessorTests()
    {
        var memoryStreamManager = new RecyclableMemoryStreamManager();
        var compressor = new GzipResourceCompressor(memoryStreamManager);

        var includeProcessor = new IncludeProcessor(
            Context,
            Cache,
            compressor,
            NullLoggerFactory.Instance.CreateLogger<IncludeProcessor>());

        var revIncludeProcessor = new RevIncludeProcessor(
            Context,
            Cache,
            compressor,
            NullLoggerFactory.Instance.CreateLogger<RevIncludeProcessor>());

        _processor = new IterateProcessor(
            includeProcessor,
            revIncludeProcessor,
            NullLoggerFactory.Instance.CreateLogger<IterateProcessor>());
    }

    [Fact]
    public async Task GivenIterateInclude_WhenChainOfReferences_ThenReturnsAllInChain()
    {
        // Arrange: Create chain Patient -> Organization -> Parent Organization
        Context.SearchParams.Add(new SearchParamEntity
        {
            SearchParamId = OrganizationPartOfSearchParamId,
            Uri = "http://hl7.org/fhir/SearchParameter/Organization-partof",
            Status = "Enabled",
            LastUpdated = DateTimeOffset.UtcNow
        });
        Context.SaveChanges();

        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var org = CreateResource(resourceTypeId: 2, resourceId: "org-1");
        CreateResource(resourceTypeId: 2, resourceId: "parent-org-1");

        // Patient -> Organization
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);

        // Organization -> Parent Organization
        CreateReference(org.ResourceSurrogateId, sourceTypeId: 2, targetTypeId: 2, targetResourceId: "parent-org-1", searchParamId: OrganizationPartOfSearchParamId);

        var mainResults = new List<SearchEntryResult> { MainResult("Patient", "patient-1") };

        // _include:iterate=Patient:organization&_include:iterate=Organization:partof
        // Both levels of the chain must be named: an iterate expression only applies to resources whose
        // type matches its source type, so Patient:organization alone cannot walk Organization -> Organization.
        var patientToOrganization = new IncludeExpression(
            resourceTypes: ["Patient"],
            referenceSearchParameter: ReferenceParameter("organization", "Patient-organization", "Organization"),
            sourceResourceType: "Patient",
            targetResourceType: "Organization",
            referencedTypes: ["Organization"],
            wildCard: false,
            reversed: false,
            iterate: true);

        var organizationToParent = new IncludeExpression(
            resourceTypes: ["Patient"],
            referenceSearchParameter: ReferenceParameter("partof", "Organization-partof", "Organization"),
            sourceResourceType: "Organization",
            targetResourceType: "Organization",
            referencedTypes: ["Organization"],
            wildCard: false,
            reversed: false,
            iterate: true);

        // Act
        var result = await _processor.ProcessIteratesAsync(
            mainResults,
            [patientToOrganization, organizationToParent],
            CancellationToken.None);

        // Assert: Should find both org-1 and parent-org-1, and not re-emit the main result
        result.Count.ShouldBe(2);
        result.ShouldContain(r => r.ResourceId == "org-1");
        result.ShouldContain(r => r.ResourceId == "parent-org-1");
    }

    [Fact]
    public async Task GivenIterateRevInclude_WhenChainOfReverseReferences_ThenReturnsAllInChain()
    {
        // Arrange: Create chain Patient <- Observation <- Encounter
        CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var obs = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        var encounter = CreateResource(resourceTypeId: 5, resourceId: "enc-1");

        // Observation -> Patient
        CreateReference(obs.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);

        // Encounter -> Observation
        CreateReference(encounter.ResourceSurrogateId, sourceTypeId: 5, targetTypeId: 3, targetResourceId: "obs-1", searchParamId: 6);

        var mainResults = new List<SearchEntryResult> { MainResult("Patient", "patient-1") };

        // _revinclude:iterate=Observation:patient
        var iterateExpression = new IncludeExpression(
            resourceTypes: ["Patient"],
            referenceSearchParameter: ReferenceParameter("patient", "Observation-patient", "Patient"),
            sourceResourceType: "Observation",
            targetResourceType: "Patient",
            referencedTypes: ["Patient"],
            wildCard: false,
            reversed: true,
            iterate: true);

        // Act
        var result = await _processor.ProcessIteratesAsync(mainResults, [iterateExpression], CancellationToken.None);

        // Assert: Observation is reached directly. Encounter is not, because no expression names
        // Encounter as a reverse-include source - iteration widens the set of source resources, not
        // the set of relationships being followed.
        result.ShouldHaveSingleItem();
        result[0].ResourceType.ShouldBe("Observation");
        result[0].ResourceId.ShouldBe("obs-1");
    }

    [Fact]
    public async Task GivenIterateInclude_WhenNoReferences_ThenReturnsEmpty()
    {
        // Arrange: Patient with no references
        CreateResource(resourceTypeId: 1, resourceId: "patient-1");

        var mainResults = new List<SearchEntryResult> { MainResult("Patient", "patient-1") };

        var iterateExpression = new IncludeExpression(
            resourceTypes: ["Patient"],
            referenceSearchParameter: ReferenceParameter("organization", "Patient-organization", "Organization"),
            sourceResourceType: "Patient",
            targetResourceType: "Organization",
            referencedTypes: ["Organization"],
            wildCard: false,
            reversed: false,
            iterate: true);

        // Act
        var result = await _processor.ProcessIteratesAsync(mainResults, [iterateExpression], CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    private static SearchEntryResult MainResult(string resourceType, string resourceId) =>
        new(
            ResourceType: resourceType,
            ResourceId: resourceId,
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            ResourceBytes: ReadOnlyMemory<byte>.Empty);

    private static SearchParameterInfo ReferenceParameter(string code, string definitionName, string targetResourceType) =>
        new(
            code,
            code,
            SearchParamType.Reference,
            new Uri($"http://hl7.org/fhir/SearchParameter/{definitionName}"),
            targetResourceTypes: [targetResourceType]);
}
