// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Microsoft.Data.SqlClient.Server;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Generates TokenSearchParamList TVP SqlDataRecord rows from token search values.
/// Token search parameters use a System|Code format (e.g., "http://system|code").
/// </summary>
public class TokenSearchParameterRowGenerator : ISearchParameterRowGenerator
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
            new SqlMetaData("SystemId", SqlDbType.Int),
            new SqlMetaData("Code", SqlDbType.VarChar, 256),
            new SqlMetaData("CodeOverflow", SqlDbType.VarChar, -1),
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
                if (searchIndex.Value is not TokenSearchValue tokenValue)
                    continue;

                // Skip tokens without a code - the Code column is NOT NULL in the database
                // Text-only tokens are indexed separately in the TokenText table
                if (string.IsNullOrEmpty(tokenValue.Code))
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Url.ToString(), out var searchParamId))
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);

                // SystemId lookup (placeholder using hash)
                record.SetInt32(3, string.IsNullOrEmpty(tokenValue.System) ? 0 : tokenValue.System.GetHashCode(StringComparison.Ordinal));

                // Handle code overflow for very long codes
                // Code is guaranteed to be non-null here due to guard at line 55
                if (tokenValue.Code.Length > 128)
                {
                    record.SetString(4, tokenValue.Code.Substring(0, 128));
                    record.SetString(5, tokenValue.Code.Substring(128));
                }
                else
                {
                    record.SetString(4, tokenValue.Code);
                    record.SetDBNull(5);
                }

                yield return record;
            }
        }
    }
}
