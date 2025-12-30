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
/// Generates UriSearchParamList TVP SqlDataRecord rows from URI search values.
/// URI search parameters store canonical URIs (case-sensitive comparison).
/// Note: The TVP only includes core columns (4 columns). Extension data (Version, Fragment)
/// is collected separately and updated via EF Core after MergeResources completes.
/// </summary>
public class UriSearchParameterRowGenerator : ISearchParameterRowGenerator
{
    public IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap)
    {
        // TVP schema matches original 97.sql definition (4 columns only)
        // Version/Fragment columns are updated post-merge via EF Core
        var metadata = new[]
        {
            new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
            new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
            new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
            new SqlMetaData("Uri", SqlDbType.VarChar, 256),
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
                if (searchIndex.Value is not UriSearchValue uriValue)
                    continue;

                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);
                // Store just the base URI (without version/fragment) for TVP
                // This matches the PK constraint in the TVP
                record.SetString(3, uriValue.Uri);

                yield return record;
            }
        }
    }

    /// <summary>
    /// Extracts extension data (Version, Fragment columns) for post-merge EF update.
    /// Returns records that have non-null Version or Fragment data.
    /// </summary>
    public IEnumerable<UriSearchParamExtensionData> ExtractExtensionData(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap)
    {
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
                if (searchIndex.Value is not UriSearchValue uriValue)
                    continue;

                // Only yield if there's Version or Fragment data to update
                if (string.IsNullOrEmpty(uriValue.Version) && string.IsNullOrEmpty(uriValue.Fragment))
                    continue;

                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                    continue;

                yield return new UriSearchParamExtensionData(
                    resourceTypeId,
                    surrogateId,
                    searchParamId,
                    uriValue.Uri,
                    uriValue.Version,
                    uriValue.Fragment);
            }
        }
    }
}

/// <summary>
/// Extension data for UriSearchParam that cannot be passed through the TVP.
/// Used for post-merge EF Core update of Version and Fragment columns.
/// </summary>
public record UriSearchParamExtensionData(
    short ResourceTypeId,
    long ResourceSurrogateId,
    short SearchParamId,
    string Uri,
    string? Version,
    string? Fragment);
