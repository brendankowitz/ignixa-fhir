// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Microsoft.Data.SqlClient.Server;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Generates ReferenceSearchParamList TVP SqlDataRecord rows from reference search values.
/// Reference search parameters store references to other resources (e.g., "Patient/123").
/// </summary>
public class ReferenceSearchParameterRowGenerator : ISearchParameterRowGenerator
{
    public IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap)
    {
        var metadata = new[]
        {
            new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
            new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
            new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
            new SqlMetaData("BaseUri", SqlDbType.VarChar, 128),
            new SqlMetaData("ReferenceResourceTypeId", SqlDbType.SmallInt),
            new SqlMetaData("ReferenceResourceId", SqlDbType.VarChar, 64),
            new SqlMetaData("ReferenceResourceVersion", SqlDbType.Int),
        };

        foreach (var resource in resources)
        {
            if (resource.SearchIndices == null || resource.SearchIndices.Count == 0)
                continue;

            if (!resourceTypeIdMap.TryGetValue(resource.ResourceType, out var resourceTypeId))
                continue;

            if (!resourceSurrogateIdMap.TryGetValue(resource, out var surrogateId))
                continue;

            // Deduplicate search indices per resource to prevent UNIQUE KEY constraint violations
            // The TVP has a UNIQUE constraint on (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId)
            var dedupSet = new HashSet<(short SearchParamId, string? BaseUri, short? ReferenceResourceTypeId, string ReferenceResourceId)>();

            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not ReferenceSearchValue refValue)
                    continue;

                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                    continue;

                // Calculate values for deduplication key
                var baseUri = refValue.BaseUri?.ToString();
                var refResourceTypeId = !string.IsNullOrEmpty(refValue.ResourceType) && resourceTypeIdMap.TryGetValue(refValue.ResourceType, out var tempRefTypeId)
                    ? (short?)tempRefTypeId
                    : null;

                // Skip if duplicate
                if (!dedupSet.Add((searchParamId, baseUri, refResourceTypeId, refValue.ResourceId)))
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);

                // BaseUri is optional for local references
                if (baseUri != null)
                    record.SetString(3, baseUri);
                else
                    record.SetDBNull(3);

                // ReferenceResourceTypeId lookup
                if (refResourceTypeId.HasValue)
                    record.SetInt16(4, refResourceTypeId.Value);
                else
                    record.SetDBNull(4);

                record.SetString(5, refValue.ResourceId);

                // Version is optional
                record.SetDBNull(6); // TODO Phase 3: Extract version if available

                yield return record;
            }
        }
    }
}
