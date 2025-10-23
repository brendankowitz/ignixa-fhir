// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Generates DateTimeSearchParamListTableType DataTable rows from datetime search values.
/// DateTime search parameters store date/time ranges with start and end times (always UTC).
/// Includes optimization for values longer than a day and min/max flags for sorting.
/// </summary>
public class DateTimeSearchParameterRowGenerator : ISearchParameterRowGenerator
{
    public DataTable CreateDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("StartDateTime", typeof(DateTimeOffset));
        table.Columns.Add("EndDateTime", typeof(DateTimeOffset));
        table.Columns.Add("IsLongerThanADay", typeof(bool));
        table.Columns.Add("IsMin", typeof(bool));
        table.Columns.Add("IsMax", typeof(bool));
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

            // Extract all datetime search indices
            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not DateTimeSearchValue dateTimeValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Code, out var searchParamId))
                    continue;

                var row = table.NewRow();
                row["ResourceTypeId"] = resourceTypeId;
                row["ResourceSurrogateId"] = surrogateId;
                row["SearchParamId"] = searchParamId;

                // Store start and end times as DateTimeOffset (preserves UTC offset)
                row["StartDateTime"] = dateTimeValue.Start;
                row["EndDateTime"] = dateTimeValue.End;

                // Optimization: flag values that span more than a day for query optimization
                var duration = dateTimeValue.End - dateTimeValue.Start;
                row["IsLongerThanADay"] = duration.TotalDays > 1;

                // Check if this value supports sorting (ISupportSortSearchValue)
                if (searchIndex.Value is ISupportSortSearchValue sortValue)
                {
                    row["IsMin"] = sortValue.IsMin;
                    row["IsMax"] = sortValue.IsMax;
                }
                else
                {
                    row["IsMin"] = false;
                    row["IsMax"] = false;
                }

                table.Rows.Add(row);
            }
        }

        return table;
    }
}
