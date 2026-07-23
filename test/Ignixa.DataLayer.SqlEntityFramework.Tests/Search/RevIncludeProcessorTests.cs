// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

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
        var memoryStreamManager = new RecyclableMemoryStreamManager();
        var compressor = new GzipResourceCompressor(memoryStreamManager);
        _processor = new RevIncludeProcessor(
            Context,
            Cache,
            compressor,
            LoggerFactory.CreateLogger<RevIncludeProcessor>());
    }

    [Fact]
    public async Task GivenRevInclude_WhenObservationsReferencePatient_ThenReturnsObservations()
    {
        // Arrange: Create Patient and Observation
        CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var observation = CreateResource(resourceTypeId: 3, resourceId: "obs-1");

        // Observation references Patient
        CreateReference(observation.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);

        var mainResults = new List<(string ResourceType, string ResourceId)>
        {
            ("Patient", "patient-1")
        };

        // Create revinclude expression: _revinclude=Observation:patient
        var revIncludeExpression = CreateObservationPatientRevInclude();

        // Act
        var result = await _processor.ProcessRevIncludesAsync(mainResults, new[] { revIncludeExpression }, CancellationToken.None);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].ResourceType.ShouldBe("Observation");
        result[0].ResourceId.ShouldBe("obs-1");
    }

    [Fact]
    public async Task GivenRevInclude_WhenMultipleObservationsReferencePatient_ThenReturnsAll()
    {
        // Arrange: Create Patient and multiple Observations
        CreateResource(resourceTypeId: 1, resourceId: "patient-1");
        var obs1 = CreateResource(resourceTypeId: 3, resourceId: "obs-1");
        var obs2 = CreateResource(resourceTypeId: 3, resourceId: "obs-2");

        // Both Observations reference Patient
        CreateReference(obs1.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);
        CreateReference(obs2.ResourceSurrogateId, sourceTypeId: 3, targetTypeId: 1, targetResourceId: "patient-1", searchParamId: 3);

        var mainResults = new List<(string ResourceType, string ResourceId)>
        {
            ("Patient", "patient-1")
        };

        var revIncludeExpression = CreateObservationPatientRevInclude();

        // Act
        var result = await _processor.ProcessRevIncludesAsync(mainResults, new[] { revIncludeExpression }, CancellationToken.None);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(r => r.ResourceId == "obs-1");
        result.ShouldContain(r => r.ResourceId == "obs-2");
    }

    [Fact]
    public async Task GivenRevInclude_WhenNoReferencingResources_ThenReturnsEmpty()
    {
        // Arrange: Create Patient with no Observations
        CreateResource(resourceTypeId: 1, resourceId: "patient-1");

        var mainResults = new List<(string ResourceType, string ResourceId)>
        {
            ("Patient", "patient-1")
        };

        var revIncludeExpression = CreateObservationPatientRevInclude();

        // Act
        var result = await _processor.ProcessRevIncludesAsync(mainResults, new[] { revIncludeExpression }, CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    private static IncludeExpression CreateObservationPatientRevInclude()
    {
        var referenceSearchParameter = new SearchParameterInfo(
            name: "patient",
            code: "patient",
            searchParamType: SearchParamType.Reference,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-patient"),
            targetResourceTypes: new[] { "Patient" });

        return new IncludeExpression(
            resourceTypes: new[] { "Patient" },
            referenceSearchParameter: referenceSearchParameter,
            sourceResourceType: "Observation",
            targetResourceType: "Patient",
            referencedTypes: new[] { "Patient" },
            wildCard: false,
            reversed: true,
            iterate: false);
    }
}
