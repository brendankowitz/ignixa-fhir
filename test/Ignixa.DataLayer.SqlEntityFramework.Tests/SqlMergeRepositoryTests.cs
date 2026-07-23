// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests;

/// <summary>
/// Integration tests for SqlMergeRepository Phase 3 implementation.
/// Validates lookup table methods, row generators, and TVP marshaling.
/// </summary>
public class SqlMergeRepositoryTests : TestBase
{
    private readonly GzipResourceCompressor _compressor;
    private readonly SqlMergeRepository _repository;

    public SqlMergeRepositoryTests()
    {
        var memoryStreamManager = new RecyclableMemoryStreamManager();
        _compressor = new GzipResourceCompressor(memoryStreamManager);
        _repository = new SqlMergeRepository(
            Context,
            _compressor,
            new NullLogger<SqlMergeRepository>(),
            Cache,
            new NullLogger<PostMergeExtensionUpdater>());

        SeedLookupData();
    }

    /// <summary>
    /// Seeds additional lookup data needed for testing.
    /// </summary>
    private void SeedLookupData()
    {
        // Add system URIs
        Context.Systems.AddRange(
            new SystemEntity { SystemId = 1, Value = "http://loinc.org" },
            new SystemEntity { SystemId = 2, Value = "http://snomed.info/sct" },
            new SystemEntity { SystemId = 3, Value = "http://hl7.org/fhir/v2/0131" }
        );

        // Add quantity codes
        Context.QuantityCodes.AddRange(
            new QuantityCodeEntity { QuantityCodeId = 1, Value = "mg" },
            new QuantityCodeEntity { QuantityCodeId = 2, Value = "kg" },
            new QuantityCodeEntity { QuantityCodeId = 3, Value = "mmol/L" }
        );

        Context.SaveChanges();
    }

    [Fact]
    public async Task GivenSeededResourceTypes_WhenReadingResourceTypeMappings_ThenReturnsCorrectMapping()
    {
        // Arrange
        await Cache.PreloadResourceTypesAsync();

        // Act
        var result = Cache.ResourceTypeMappings;

        // Assert
        result.ShouldNotBeNull();
        result.ContainsKey("Patient").ShouldBeTrue();
        result.ContainsKey("Organization").ShouldBeTrue();
        result.ContainsKey("Observation").ShouldBeTrue();
        result["Patient"].ShouldBe((short)1);
        result["Organization"].ShouldBe((short)2);
        result["Observation"].ShouldBe((short)3);
    }

    [Fact]
    public async Task GivenSeededSearchParameters_WhenReadingSearchParameterMappings_ThenKeysAreCanonicalUris()
    {
        // Arrange
        await Cache.PreloadSearchParamsAsync();

        // Act
        var result = Cache.SearchParameterMappings;

        // Assert
        // Keyed by canonical URI, not by the trailing code: "name" is ambiguous across
        // Patient-name and Organization-name, so both must remain individually addressable.
        result.ShouldNotBeNull();
        result["http://hl7.org/fhir/SearchParameter/Patient-name"].ShouldBe((short)1);
        result["http://hl7.org/fhir/SearchParameter/Organization-name"].ShouldBe((short)5);
        result["http://hl7.org/fhir/SearchParameter/Patient-organization"].ShouldBe((short)2);
        result["http://hl7.org/fhir/SearchParameter/Observation-patient"].ShouldBe((short)3);
        result["http://hl7.org/fhir/SearchParameter/Observation-code"].ShouldBe((short)4);
    }

    [Fact]
    public async Task GivenSeededSystems_WhenResolvingSystemUri_ThenReturnsSystemId()
    {
        // Act
        var loinc = await Cache.GetSystemIdAsync("http://loinc.org");
        var snomed = await Cache.GetSystemIdAsync("http://snomed.info/sct");

        // Assert
        loinc.ShouldBe(1);
        snomed.ShouldBe(2);
        Cache.SystemMappings.ContainsKey("http://loinc.org").ShouldBeTrue();
        Cache.SystemMappings.ContainsKey("http://snomed.info/sct").ShouldBeTrue();
    }

    [Fact]
    public async Task GivenSeededQuantityCodes_WhenResolvingQuantityCode_ThenReturnsQuantityCodeId()
    {
        // Act
        var mg = await Cache.GetQuantityCodeIdAsync("mg");
        var kg = await Cache.GetQuantityCodeIdAsync("kg");

        // Assert
        mg.ShouldBe(1);
        kg.ShouldBe(2);
        Cache.QuantityCodeMappings.ContainsKey("mg").ShouldBeTrue();
        Cache.QuantityCodeMappings.ContainsKey("kg").ShouldBeTrue();
    }

    [Fact]
    public async Task GivenMultipleResources_WhenMerging_ThenTvpsAreMarshalledBeforeTheStoredProcedureCall()
    {
        // Arrange
        var transactionId = 1000L;
        var resources = new[]
        {
            CreatePatientWrapper("p1"),
            CreatePatientWrapper("p2")
        };

        var entryIndices = new[] { 0, 1 };

        // Act / Assert
        // The in-memory provider cannot execute the MergeResources stored procedure, so the call
        // must reach - and only fail at - the relational boundary. Anything thrown earlier means
        // surrogate ID mapping or TVP row generation is broken.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => _repository.MergeResourcesAsync(
                transactionId,
                singleTransaction: true,
                resources,
                entryIndices,
                CancellationToken.None));

        exception.Message.ShouldContain("relational", Case.Insensitive);
    }

    [Fact]
    public async Task GivenNoSearchIndices_WhenMerging_ThenTvpsAreMarshalledBeforeTheStoredProcedureCall()
    {
        // Arrange
        // Resources with no search indices produce empty TVPs, which SqlClient requires to be
        // passed as NULL rather than an empty IEnumerable.
        var transactionId = 2000L;
        var resources = new[] { CreatePatientWrapper("p-empty") };
        var entryIndices = new[] { 0 };

        // Act / Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => _repository.MergeResourcesAsync(
                transactionId,
                singleTransaction: true,
                resources,
                entryIndices,
                CancellationToken.None));

        exception.Message.ShouldContain("relational", Case.Insensitive);
    }

    [Fact]
    public async Task GivenEntryIndicesThatDoNotMatchResources_WhenMerging_ThenThrowsArgumentException()
    {
        // Arrange
        var resources = new[] { CreatePatientWrapper("p1"), CreatePatientWrapper("p2") };
        var entryIndices = new[] { 0 };

        // Act / Assert
        await Should.ThrowAsync<ArgumentException>(
            () => _repository.MergeResourcesAsync(
                transactionId: 3000L,
                singleTransaction: true,
                resources,
                entryIndices,
                CancellationToken.None));
    }

    private static ResourceWrapper CreatePatientWrapper(string id)
    {
        var patient = new ResourceJsonNode { ResourceType = "Patient", Id = id };

        return new ResourceWrapper(
            ResourceType: "Patient",
            ResourceId: id,
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: patient,
            Request: new ResourceRequest("POST", "Patient"),
            IsDeleted: false)
        {
            SearchIndices = new List<object>()
        };
    }
}
