// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

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
        CreateResource(resourceTypeId: 2, resourceId: "org-1");

        // Patient references Organization
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);

        var mainResults = new List<(string ResourceType, string ResourceId)>
        {
            ("Patient", "patient-1")
        };

        // Create include expression: _include=Patient:organization
        var includeExpression = new IncludeExpression(
            resourceTypes: ["Patient"],
            referenceSearchParameter: PatientOrganizationParameter(),
            sourceResourceType: "Patient",
            targetResourceType: "Organization",
            referencedTypes: ["Organization"],
            wildCard: false,
            reversed: false,
            iterate: false);

        // Act
        var result = await _processor.ProcessIncludesAsync(mainResults, [includeExpression], CancellationToken.None);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].ResourceType.ShouldBe("Organization");
        result[0].ResourceId.ShouldBe("org-1");
        result[0].SearchMode.ShouldBe(Ignixa.Domain.Models.SearchEntryMode.Include);
    }

    [Fact]
    public async Task GivenWildcardInclude_WhenPatientHasMultipleReferences_ThenReturnsAllReferenced()
    {
        // Arrange: Create Patient, Organization, and Practitioner
        var patient = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        CreateResource(resourceTypeId: 2, resourceId: "org-1");
        CreateResource(resourceTypeId: 4, resourceId: "pract-1");

        // Patient references both
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);
        CreateReference(patient.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 4, targetResourceId: "pract-1", searchParamId: 1);

        var mainResults = new List<(string ResourceType, string ResourceId)>
        {
            ("Patient", "patient-1")
        };

        // Create wildcard include: _include=Patient:*
        var includeExpression = new IncludeExpression(
            resourceTypes: ["Patient"],
            referenceSearchParameter: null!,
            sourceResourceType: "Patient",
            targetResourceType: null!,
            referencedTypes: ["Organization", "Practitioner"],
            wildCard: true,
            reversed: false,
            iterate: false);

        // Act
        var result = await _processor.ProcessIncludesAsync(mainResults, [includeExpression], CancellationToken.None);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(r => r.ResourceType == "Organization" && r.ResourceId == "org-1");
        result.ShouldContain(r => r.ResourceType == "Practitioner" && r.ResourceId == "pract-1");
    }

    [Fact]
    public async Task GivenInclude_WhenNoDuplicates_ThenReturnsSingleResource()
    {
        // Arrange: Two Patients reference same Organization
        var patient1 = CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var patient2 = CreateResource(resourceTypeId: 1, resourceId: "patient-2");
        CreateResource(resourceTypeId: 2, resourceId: "org-1");

        CreateReference(patient1.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);
        CreateReference(patient2.ResourceSurrogateId, sourceTypeId: 1, targetTypeId: 2, targetResourceId: "org-1", searchParamId: 2);

        var mainResults = new List<(string ResourceType, string ResourceId)>
        {
            ("Patient", "patient-1"),
            ("Patient", "patient-2")
        };

        var includeExpression = new IncludeExpression(
            resourceTypes: ["Patient"],
            referenceSearchParameter: PatientOrganizationParameter(),
            sourceResourceType: "Patient",
            targetResourceType: "Organization",
            referencedTypes: ["Organization"],
            wildCard: false,
            reversed: false,
            iterate: false);

        // Act
        var result = await _processor.ProcessIncludesAsync(mainResults, [includeExpression], CancellationToken.None);

        // Assert: Should only return Organization once (deduplication)
        result.ShouldHaveSingleItem();
        result[0].ResourceId.ShouldBe("org-1");
    }

    private static SearchParameterInfo PatientOrganizationParameter() =>
        new(
            "organization",
            "organization",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);
}
