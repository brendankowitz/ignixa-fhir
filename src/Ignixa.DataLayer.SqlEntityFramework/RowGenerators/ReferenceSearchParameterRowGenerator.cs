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
    private int _recordCount;

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

            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not ReferenceSearchValue refValue)
                    continue;

                var searchParamUrl = searchIndex.SearchParameter.Url.ToString();
                if (!searchParameterIdMap.TryGetValue(searchParamUrl, out var searchParamId))
                {
                    System.Diagnostics.Debug.WriteLine($"ReferenceSearchParameterRowGenerator: WARNING - SearchParam URL not found in map: {searchParamUrl} for resource {resource.ResourceType}/{resource.ResourceId}");
                    continue;
                }

                System.Diagnostics.Debug.WriteLine($"ReferenceSearchParameterRowGenerator: Matched URL={searchParamUrl} → SearchParamId={searchParamId} for {resource.ResourceType}/{resource.ResourceId}");

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);

                // BaseUri is optional for local references
                if (refValue.BaseUri != null)
                    record.SetString(3, refValue.BaseUri.ToString());
                else
                    record.SetDBNull(3);

                // ReferenceResourceTypeId lookup
                if (!string.IsNullOrEmpty(refValue.ResourceType) && resourceTypeIdMap.TryGetValue(refValue.ResourceType, out var refResourceTypeId))
                    record.SetInt16(4, refResourceTypeId);
                else
                    record.SetDBNull(4);

                record.SetString(5, refValue.ResourceId);

                // Version is optional
                record.SetDBNull(6); // TODO Phase 3: Extract version if available

                _recordCount++;
                System.Diagnostics.Debug.WriteLine($"ReferenceSearchParameterRowGenerator: Yielding record #{_recordCount} - ResourceType={resourceTypeId}, SearchParam={searchParamId}, RefResourceType={record.GetInt16(4)}, RefResourceId={refValue.ResourceId}");
                yield return record;
            }
        }

        System.Diagnostics.Debug.WriteLine($"ReferenceSearchParameterRowGenerator: Total records generated: {_recordCount}");
    }
}
