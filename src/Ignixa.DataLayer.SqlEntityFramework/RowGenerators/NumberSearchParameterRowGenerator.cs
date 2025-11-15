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
/// Generates NumberSearchParamList TVP SqlDataRecord rows from number search values.
/// Number search parameters store numeric values with optional range bounds (low/high).
/// </summary>
public class NumberSearchParameterRowGenerator : ISearchParameterRowGenerator
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
            new SqlMetaData("SingleValue", SqlDbType.Decimal),
            new SqlMetaData("LowValue", SqlDbType.Decimal),
            new SqlMetaData("HighValue", SqlDbType.Decimal),
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
                if (searchIndex.Value is not NumberSearchValue numberValue)
                    continue;

                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);

                if (numberValue.Low.HasValue && numberValue.High.HasValue && numberValue.Low == numberValue.High)
                {
                    record.SetDecimal(3, numberValue.Low.Value);
                    record.SetDBNull(4);
                    record.SetDBNull(5);
                }
                else
                {
                    record.SetDBNull(3);
                    if (numberValue.Low.HasValue)
                        record.SetDecimal(4, numberValue.Low.Value);
                    else
                        record.SetDBNull(4);
                    if (numberValue.High.HasValue)
                        record.SetDecimal(5, numberValue.High.Value);
                    else
                        record.SetDBNull(5);
                }

                yield return record;
            }
        }
    }
}
