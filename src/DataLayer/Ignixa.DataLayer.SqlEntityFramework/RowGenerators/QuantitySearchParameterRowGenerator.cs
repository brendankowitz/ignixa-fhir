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

                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);
                record.SetInt32(3, string.IsNullOrEmpty(quantityValue.System) ? 0 : quantityValue.System.GetHashCode(StringComparison.Ordinal));
                record.SetInt32(4, string.IsNullOrEmpty(quantityValue.Code) ? 0 : quantityValue.Code.GetHashCode(StringComparison.Ordinal));

                if (quantityValue.Low.HasValue && quantityValue.High.HasValue && quantityValue.Low == quantityValue.High)
                {
                    // Single value - exact match
                    // Populate all three columns with the same value to satisfy NOT NULL constraints
                    record.SetDecimal(5, quantityValue.Low.Value);  // SingleValue
                    record.SetDecimal(6, quantityValue.Low.Value);  // LowValue (same value)
                    record.SetDecimal(7, quantityValue.Low.Value);  // HighValue (same value)
                }
                else
                {
                    // Range or single-sided value
                    record.SetDBNull(5);                           // SingleValue = NULL

                    // For range values, at least one of Low/High must be non-null per QuantitySearchValue constructor
                    // If only one is provided, use it for both Low and High to satisfy NOT NULL constraints
                    var lowValue = quantityValue.Low ?? quantityValue.High ?? throw new InvalidOperationException("Both Low and High are null");
                    var highValue = quantityValue.High ?? quantityValue.Low ?? throw new InvalidOperationException("Both High and Low are null");

                    record.SetDecimal(6, lowValue);
                    record.SetDecimal(7, highValue);
                }

                yield return record;
            }
        }
    }
}
