// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Generates ResourceList TVP SqlDataRecord rows from ResourceWrapper list.
/// Resources are the main FHIR resource records with versioning and metadata.
/// </summary>
public class ResourceRowGenerator
{
    private readonly GzipResourceCompressor _compressor;
    private readonly ILogger<ResourceRowGenerator>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceRowGenerator"/> class.
    /// </summary>
    /// <param name="compressor">The Gzip resource compressor.</param>
    /// <param name="logger">Optional logger instance.</param>
    public ResourceRowGenerator(GzipResourceCompressor compressor, ILogger<ResourceRowGenerator>? logger = null)
    {
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _logger = logger;
    }

    /// <summary>
    /// Generates SqlDataRecord rows for dbo.ResourceList TVP.
    /// </summary>
    /// <param name="transactionId">The transaction ID (used to calculate surrogate IDs).</param>
    /// <param name="resources">The resources to generate rows for.</param>
    /// <param name="resourceTypeIdMap">Mapping from resource type name to resource type ID.</param>
    /// <param name="entryIndices">Bundle entry indices for surrogate ID calculation.</param>
    /// <returns>Enumerable of SqlDataRecord rows.</returns>
    public IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        long transactionId,
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyList<int> entryIndices)
    {
        // Column order MUST match dbo.ResourceList SQL type definition exactly
        var metadata = new[]
        {
            new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
            new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
            new SqlMetaData("ResourceId", SqlDbType.VarChar, 64),
            new SqlMetaData("Version", SqlDbType.Int),
            new SqlMetaData("HasVersionToCompare", SqlDbType.Bit),
            new SqlMetaData("IsDeleted", SqlDbType.Bit),
            new SqlMetaData("IsHistory", SqlDbType.Bit),
            new SqlMetaData("KeepHistory", SqlDbType.Bit),
            new SqlMetaData("RawResource", SqlDbType.VarBinary, -1), // -1 = MAX
            new SqlMetaData("IsRawResourceMetaSet", SqlDbType.Bit),
            new SqlMetaData("RequestMethod", SqlDbType.VarChar, 10),
            new SqlMetaData("SearchParamHash", SqlDbType.VarChar, 64),
        };

        for (int index = 0; index < resources.Count; index++)
        {
            var resource = resources[index];

            // Look up ResourceTypeId from map
            if (!resourceTypeIdMap.TryGetValue(resource.ResourceType, out var resourceTypeId))
            {
                _logger?.LogWarning(
                    "ResourceType '{ResourceType}' not found in lookup table, skipping resource {ResourceId}",
                    resource.ResourceType,
                    resource.ResourceId);
                continue;
            }

            var record = new SqlDataRecord(metadata);

            record.SetInt16(0, resourceTypeId);

            // Allocate surrogate ID from the reserved sequence range
            // Pattern: transactionId + entryIndex (bundle entry position)
            // CRITICAL: Use entryIndices[index] NOT loop index to ensure unique IDs across bundle batches
            record.SetInt64(1, transactionId + entryIndices[index]);

            record.SetString(2, resource.ResourceId);

            var version = int.Parse(resource.VersionId);
            record.SetInt32(3, version);

            // HasVersionToCompare logic:
            // - POST: Always false (creates never compare versions)
            // - PUT with version = 1: False (new resource)
            // - PUT with version > 1: True (updating existing resource)
            var isPost = string.Equals(resource.Request.Method, "POST", StringComparison.OrdinalIgnoreCase);
            record.SetBoolean(4, !isPost && version > 1);

            record.SetBoolean(5, resource.IsDeleted);
            record.SetBoolean(6, false); // IsHistory: False for current version, true for history entries
            record.SetBoolean(7, true);  // KeepHistory: Always keep history (configurable in production)

            var compressedResource = _compressor.SerializeAndCompress(resource.Resource);
            record.SetBytes(8, 0, compressedResource, 0, compressedResource.Length);

            record.SetBoolean(9, true); // IsRawResourceMetaSet

            record.SetString(10, resource.Request.Method.ToString());

            // SearchParamHash: TODO Phase 2
            record.SetDBNull(11);

            yield return record;
        }
    }
}
