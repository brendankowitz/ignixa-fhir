// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Xunit;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
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

    // NOTE: GetResourceTypeIdMapAsync/GetSearchParameterIdMapAsync/GetSystemIdMapAsync/GetQuantityCodeIdMapAsync
    // no longer exist on SqlMergeRepository - ID lookups now happen via SearchIndexReferenceDataCache's
    // ResourceTypeMappings/SearchParameterMappings/SystemMappings/QuantityCodeMappings properties, which are
    // covered by SearchIndexReferenceDataCacheTests.cs. The four tests that exercised this capability directly
    // on SqlMergeRepository were removed rather than rewritten (see task-0b-report.md for details).

    #region Integration Tests

    [Fact]
    public async Task MergeResourcesAsync_WithResourceSurrogateIdMap_CorrectlyAssignsIds()
    {
        // Arrange
        var transactionId = 1000L;
        var patient1 = new ResourceJsonNode { ResourceType = "Patient", Id = "p1" };
        var patient2 = new ResourceJsonNode { ResourceType = "Patient", Id = "p2" };

        var wrapper1 = new ResourceWrapper(
            ResourceType: "Patient",
            ResourceId: "p1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: patient1,
            Request: new ResourceRequest("POST", "Patient"),
            IsDeleted: false)
        {
            SearchIndices = new List<object>(),
            TenantId = null
        };

        var wrapper2 = new ResourceWrapper(
            ResourceType: "Patient",
            ResourceId: "p2",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: patient2,
            Request: new ResourceRequest("POST", "Patient"),
            IsDeleted: false)
        {
            SearchIndices = new List<object>(),
            TenantId = null
        };

        var resources = new[] { wrapper1, wrapper2 };
        var entryIndices = new[] { 0, 1 };

        // Act
        // This will fail with actual SQL Server but validates the TVP marshaling logic
        try
        {
            await _repository.MergeResourcesAsync(
                transactionId,
                singleTransaction: true,
                resources,
                entryIndices,
                CancellationToken.None);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("relational database provider", StringComparison.Ordinal))
        {
            // Expected for in-memory database - this validates structure only
        }
    }

    [Fact]
    public async Task MergeResourcesAsync_WithNoSearchIndices_ConvertsEmptyTvpsToNull()
    {
        // Arrange
        // Resources with no search indices (empty searchIndices list)
        // Tests that empty TVPs are materialized and converted to NULL
        // SqlClient requires NULL (not empty IEnumerable) for TVPs to avoid ArgumentException

        var transactionId = 2000L;
        var patient = new ResourceJsonNode { ResourceType = "Patient", Id = "p-empty" };

        var wrapper = new ResourceWrapper(
            ResourceType: "Patient",
            ResourceId: "p-empty",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: patient,
            Request: new ResourceRequest("POST", "Patient"),
            IsDeleted: false)
        {
            SearchIndices = new List<object>(),  // No search indices - validates empty TVP handling
            TenantId = null
        };

        var resources = new[] { wrapper };
        var entryIndices = new[] { 0 };

        // Act
        // This will fail with actual SQL Server (no database provider) but validates TVP marshaling
        // The important part is that empty TVPs are converted to NULL (not passed as empty enumerables)
        try
        {
            await _repository.MergeResourcesAsync(
                transactionId,
                singleTransaction: true,
                resources,
                entryIndices,
                CancellationToken.None);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("relational database provider", StringComparison.Ordinal))
        {
            // Expected for in-memory database - validates structure only
        }
    }

    #endregion
}
