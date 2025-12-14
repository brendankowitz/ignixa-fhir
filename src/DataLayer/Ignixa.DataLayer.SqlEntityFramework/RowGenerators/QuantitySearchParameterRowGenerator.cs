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
    // Defaults for unbounded ranges in DECIMAL(36, 18) columns
    // Note: The table requires NOT NULL for LowValue/HighValue, so we use sentinel values
    // when the QuantitySearchValue has only one bound (e.g., ">= 10 mg" has Low but no High)
    private const decimal DefaultLowValue = -999999999999999999m;
    private const decimal DefaultHighValue = 999999999999999999m;

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
            // CRITICAL: Specify precision (36) and scale (18) to match database column definition
            // Without explicit precision/scale, SqlMetaData defaults to (18, 0) which truncates decimals!
            new SqlMetaData("SingleValue", SqlDbType.Decimal, precision: 36, scale: 18),
            new SqlMetaData("LowValue", SqlDbType.Decimal, precision: 36, scale: 18),
            new SqlMetaData("HighValue", SqlDbType.Decimal, precision: 36, scale: 18),
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
            // The TVP has a UNIQUE constraint on (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue)
            var dedupSet = new HashSet<(short SearchParamId, int SystemId, int QuantityCodeId, decimal? SingleValue, decimal LowValue, decimal HighValue)>();

            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not QuantitySearchValue quantityValue)
                    continue;

                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                    continue;

                // Calculate values for this row
                var systemId = string.IsNullOrEmpty(quantityValue.System) ? 0 : quantityValue.System.GetHashCode(StringComparison.Ordinal);
                var quantityCodeId = string.IsNullOrEmpty(quantityValue.Code) ? 0 : quantityValue.Code.GetHashCode(StringComparison.Ordinal);

                decimal? singleValue;
                decimal lowValue;
                decimal highValue;

                if (quantityValue.Low.HasValue && quantityValue.High.HasValue && quantityValue.Low == quantityValue.High)
                {
                    // Exact match: store in SingleValue and duplicate to Low/High
                    var value = quantityValue.Low.Value;
                    singleValue = value;
                    lowValue = value;
                    highValue = value;
                }
                else
                {
                    // Range: SingleValue is null, Low/High use defaults if unbounded
                    singleValue = null;
                    lowValue = quantityValue.Low ?? DefaultLowValue;
                    highValue = quantityValue.High ?? DefaultHighValue;
                }

                // Skip if duplicate
                if (!dedupSet.Add((searchParamId, systemId, quantityCodeId, singleValue, lowValue, highValue)))
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);
                record.SetInt32(3, systemId);
                record.SetInt32(4, quantityCodeId);

                if (singleValue.HasValue)
                {
                    record.SetDecimal(5, singleValue.Value);
                    record.SetDecimal(6, lowValue);
                    record.SetDecimal(7, highValue);
                }
                else
                {
                    record.SetDBNull(5);
                    record.SetDecimal(6, lowValue);
                    record.SetDecimal(7, highValue);
                }

                yield return record;
            }
        }
    }
}
