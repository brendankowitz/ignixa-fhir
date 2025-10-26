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
/// Generates QuantitySearchParamList TVP SqlDataRecord rows from quantity search values.
/// Quantity search parameters store numeric values with system and code (unit) information.
/// Includes optional range bounds (low/high).
/// </summary>
public class QuantitySearchParameterRowGenerator : ISearchParameterRowGenerator
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
            new SqlMetaData("QuantityCodeId", SqlDbType.Int),
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
                if (searchIndex.Value is not QuantitySearchValue quantityValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Url.ToString(), out var searchParamId))
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);
                record.SetInt32(3, string.IsNullOrEmpty(quantityValue.System) ? 0 : quantityValue.System.GetHashCode(StringComparison.Ordinal));
                record.SetInt32(4, string.IsNullOrEmpty(quantityValue.Code) ? 0 : quantityValue.Code.GetHashCode(StringComparison.Ordinal));

                if (quantityValue.Low.HasValue && quantityValue.High.HasValue && quantityValue.Low == quantityValue.High)
                {
                    record.SetDecimal(5, quantityValue.Low.Value);
                    record.SetDBNull(6);
                    record.SetDBNull(7);
                }
                else
                {
                    record.SetDBNull(5);
                    if (quantityValue.Low.HasValue)
                        record.SetDecimal(6, quantityValue.Low.Value);
                    else
                        record.SetDBNull(6);
                    if (quantityValue.High.HasValue)
                        record.SetDecimal(7, quantityValue.High.Value);
                    else
                        record.SetDBNull(7);
                }

                yield return record;
            }
        }
    }
}
