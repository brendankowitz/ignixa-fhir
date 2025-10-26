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
/// Generates StringSearchParamList TVP SqlDataRecord rows from string search values.
/// String search parameters store text values with optional overflow for very long strings.
/// Supports min/max flags for sorting optimization.
/// </summary>
public class StringSearchParameterRowGenerator : ISearchParameterRowGenerator
{
    private const int StringColumnMaxLength = 128;

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
            new SqlMetaData("Text", SqlDbType.NVarChar, 256),
            new SqlMetaData("TextOverflow", SqlDbType.NVarChar, -1),
            new SqlMetaData("IsMin", SqlDbType.Bit),
            new SqlMetaData("IsMax", SqlDbType.Bit),
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
                if (searchIndex.Value is not StringSearchValue stringValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Url.ToString(), out var searchParamId))
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);

                var textValue = stringValue.String;
                if (textValue != null && textValue.Length > StringColumnMaxLength)
                {
                    record.SetString(3, textValue.Substring(0, StringColumnMaxLength));
                    record.SetString(4, textValue.Substring(StringColumnMaxLength));
                }
                else
                {
                    if (textValue != null)
                        record.SetString(3, textValue);
                    else
                        record.SetDBNull(3);
                    record.SetDBNull(4);
                }

                if (searchIndex.Value is ISupportSortSearchValue sortValue)
                {
                    record.SetBoolean(5, sortValue.IsMin);
                    record.SetBoolean(6, sortValue.IsMax);
                }
                else
                {
                    record.SetBoolean(5, false);
                    record.SetBoolean(6, false);
                }

                yield return record;
            }
        }
    }
}
