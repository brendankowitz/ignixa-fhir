// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Generates QuantityCodeTableType DataTable rows for quantity search parameter unit/code references.
/// Stores the mapping between a quantity search value and its associated code/unit system.
/// Used separately from the main QuantitySearchParam table to normalize code lookups.
/// </summary>
public class QuantityCodeRowGenerator : ISearchParameterRowGenerator
{
    public DataTable CreateDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("QuantityCodeId", typeof(int));
        table.Columns.Add("SystemId", typeof(int));
        table.Columns.Add("Code", typeof(string));
        return table;
    }

    public DataTable GenerateRows(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap)
    {
        var table = CreateDataTable();

        foreach (var resource in resources)
        {
            if (resource.SearchIndices == null || resource.SearchIndices.Count == 0)
                continue;

            // Look up surrogate ID from map
            if (!resourceSurrogateIdMap.TryGetValue(resource, out var surrogateId))
                continue; // Skip if not found in map

            // Extract all quantity search indices
            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not QuantitySearchValue quantityValue)
                    continue;

                var row = table.NewRow();
                row["ResourceSurrogateId"] = surrogateId;

                // QuantityCodeId lookup would be implemented in Phase 3
                // For now, use hash of code string as placeholder
                row["QuantityCodeId"] = string.IsNullOrEmpty(quantityValue.Code) ? 0 : quantityValue.Code.GetHashCode(StringComparison.Ordinal);

                // SystemId lookup would be implemented in Phase 3
                // For now, use hash of system string as placeholder, or DBNull if null
                row["SystemId"] = string.IsNullOrEmpty(quantityValue.System)
                    ? (object)DBNull.Value
                    : quantityValue.System.GetHashCode(StringComparison.Ordinal);

                // Code is the actual code value
                row["Code"] = string.IsNullOrEmpty(quantityValue.Code)
                    ? (object)DBNull.Value
                    : quantityValue.Code;

                table.Rows.Add(row);
            }
        }

        return table;
    }
}
