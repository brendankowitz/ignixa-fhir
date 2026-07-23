// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests;

/// <summary>
/// Base class for integration tests providing an in-memory database context.
/// </summary>
public abstract class TestBase : IDisposable
{
    protected FhirDbContext Context { get; private set; }
    protected SearchIndexReferenceDataCache Cache { get; private set; }
    protected IFhirRepository MockRepository { get; private set; }
    protected ILoggerFactory LoggerFactory { get; private set; }

    private bool _disposed;

    protected TestBase()
    {
        // Create in-memory database with unique name for each test
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        Context = new FhirDbContext(options);
        LoggerFactory = new NullLoggerFactory();
        Cache = new SearchIndexReferenceDataCache(Context, LoggerFactory.CreateLogger<SearchIndexReferenceDataCache>());
        MockRepository = Substitute.For<IFhirRepository>();

        // Seed common reference data
        SeedReferenceData();
    }

    private void SeedReferenceData()
    {
        // Add common resource types
        Context.ResourceTypes.AddRange(
            new ResourceTypeEntity { ResourceTypeId = 1, Name = "Patient" },
            new ResourceTypeEntity { ResourceTypeId = 2, Name = "Organization" },
            new ResourceTypeEntity { ResourceTypeId = 3, Name = "Observation" },
            new ResourceTypeEntity { ResourceTypeId = 4, Name = "Practitioner" },
            new ResourceTypeEntity { ResourceTypeId = 5, Name = "Encounter" }
        );

        // Status is non-nullable on SearchParamEntity, so EF rejects the insert without it. "Enabled" is
        // what SearchIndexReferenceDataCache writes when it seeds a parameter.
        Context.SearchParams.AddRange(
            NewSearchParam(1, "http://hl7.org/fhir/SearchParameter/Patient-name"),
            NewSearchParam(2, "http://hl7.org/fhir/SearchParameter/Patient-organization"),
            NewSearchParam(3, "http://hl7.org/fhir/SearchParameter/Observation-patient"),
            NewSearchParam(4, "http://hl7.org/fhir/SearchParameter/Observation-code"),
            NewSearchParam(5, "http://hl7.org/fhir/SearchParameter/Organization-name"),
            NewSearchParam(6, "http://hl7.org/fhir/SearchParameter/Encounter-subject")
        );

        Context.SaveChanges();
    }

    /// <summary>
    /// Creates a resource entity in the database.
    /// </summary>
    protected ResourceEntity CreateResource(short resourceTypeId, string resourceId, int version = 1, bool isHistory = false, bool isDeleted = false)
    {
        // Minimal RawResource payload. GzipResourceCompressor.SerializeAndCompress takes a
        // ResourceJsonNode, which is more machinery than a fixture needs; gzipping the bytes directly
        // produces exactly what its DecompressBytes reads back.
        var minimalJson = @"{""resourceType"":""Resource"",""id"":""" + resourceId + @"""}";
        var compressedBytes = GzipCompress(System.Text.Encoding.UTF8.GetBytes(minimalJson));

        var resource = new ResourceEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceId = resourceId,
            Version = version,
            IsHistory = isHistory,
            IsDeleted = isDeleted,
            ResourceSurrogateId = GenerateSurrogateId(),
            RawResource = compressedBytes
        };

        Context.Resources.Add(resource);
        Context.SaveChanges();

        return resource;
    }

    /// <summary>
    /// Creates a reference between two resources.
    /// </summary>
    protected void CreateReference(
        long sourceSurrogateId,
        short sourceTypeId,
        short targetTypeId,
        string targetResourceId,
        short searchParamId)
    {
        var reference = new ReferenceSearchParamEntity
        {
            ResourceTypeId = sourceTypeId,
            ResourceSurrogateId = sourceSurrogateId,
            SearchParamId = searchParamId,
            ReferenceResourceTypeId = targetTypeId,
            ReferenceResourceId = targetResourceId
        };

        Context.ReferenceSearchParams.Add(reference);
        Context.SaveChanges();
    }

    /// <summary>
    /// Creates a string search parameter entry.
    /// </summary>
    protected void CreateStringSearchParam(long resourceSurrogateId, short resourceTypeId, short searchParamId, string text)
    {
        var param = new StringSearchParamEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceSurrogateId = resourceSurrogateId,
            SearchParamId = searchParamId,
            Text = text,
            IsMin = false,
            IsMax = false
        };

        Context.StringSearchParams.Add(param);
        Context.SaveChanges();
    }

    private static long _surrogateIdCounter = 1000;
    private static long GenerateSurrogateId() => Interlocked.Increment(ref _surrogateIdCounter);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Context?.Dispose();
            }

            _disposed = true;
        }
    }

    private static SearchParamEntity NewSearchParam(short searchParamId, string uri) => new()
    {
        SearchParamId = searchParamId,
        Uri = uri,
        Status = "Enabled",
        LastUpdated = DateTimeOffset.UtcNow,
    };

    private static byte[] GzipCompress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }
}
