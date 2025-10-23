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
/// Generates NumberSearchParamListTableType DataTable rows from number search values.
/// Number search parameters store numeric values with optional range bounds (low/high).
/// </summary>
public class NumberSearchParameterRowGenerator : ISearchParameterRowGenerator
{
    public DataTable CreateDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("SingleValue", typeof(decimal));
        table.Columns.Add("LowValue", typeof(decimal));
        table.Columns.Add("HighValue", typeof(decimal));
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

            if (!resourceTypeIdMap.TryGetValue(resource.ResourceType, out var resourceTypeId))
                continue;

            // Look up surrogate ID from map
            if (!resourceSurrogateIdMap.TryGetValue(resource, out var surrogateId))
                continue; // Skip if not found in map

            // Extract all number search indices
            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not NumberSearchValue numberValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Code, out var searchParamId))
                    continue;

                var row = table.NewRow();
                row["ResourceTypeId"] = resourceTypeId;
                row["ResourceSurrogateId"] = surrogateId;
                row["SearchParamId"] = searchParamId;

                // If low == high, store in SingleValue; otherwise use range
                if (numberValue.Low.HasValue && numberValue.High.HasValue && numberValue.Low == numberValue.High)
                {
                    row["SingleValue"] = numberValue.Low.Value;
                    row["LowValue"] = DBNull.Value;
                    row["HighValue"] = DBNull.Value;
                }
                else
                {
                    row["SingleValue"] = DBNull.Value;
                    row["LowValue"] = numberValue.Low ?? (object)DBNull.Value;
                    row["HighValue"] = numberValue.High ?? (object)DBNull.Value;
                }

                table.Rows.Add(row);
            }
        }

        return table;
    }
}
