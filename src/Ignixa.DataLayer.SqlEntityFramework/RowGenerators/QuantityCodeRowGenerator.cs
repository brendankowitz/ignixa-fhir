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
/// Generates QuantityCodeTableType DataTable rows for quantity search parameter unit/code references.
/// Stores the mapping between a quantity search value and its associated code/unit system.
/// Used separately from the main QuantitySearchParam table to normalize code lookups.
/// </summary>
public class QuantityCodeRowGenerator : ISearchParameterRowGenerator
{
    public IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap)
    {
        var metadata = new[]
        {
            new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
            new SqlMetaData("QuantityCodeId", SqlDbType.Int),
            new SqlMetaData("SystemId", SqlDbType.Int),
            new SqlMetaData("Code", SqlDbType.VarChar, 256),
        };

        foreach (var resource in resources)
        {
            if (resource.SearchIndices == null || resource.SearchIndices.Count == 0)
                continue;

            if (!resourceSurrogateIdMap.TryGetValue(resource, out var surrogateId))
                continue;

            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not QuantitySearchValue quantityValue)
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt64(0, surrogateId);

                // QuantityCodeId lookup would be implemented in Phase 3
                // For now, use hash of code string as placeholder
                record.SetInt32(1, string.IsNullOrEmpty(quantityValue.Code) ? 0 : quantityValue.Code.GetHashCode(StringComparison.Ordinal));

                // SystemId lookup would be implemented in Phase 3
                // For now, use hash of system string as placeholder, or DBNull if null
                if (string.IsNullOrEmpty(quantityValue.System))
                {
                    record.SetDBNull(2);
                }
                else
                {
                    record.SetInt32(2, quantityValue.System.GetHashCode(StringComparison.Ordinal));
                }

                // Code is the actual code value
                if (string.IsNullOrEmpty(quantityValue.Code))
                {
                    record.SetDBNull(3);
                }
                else
                {
                    record.SetString(3, quantityValue.Code);
                }

                yield return record;
            }
        }
    }
}
