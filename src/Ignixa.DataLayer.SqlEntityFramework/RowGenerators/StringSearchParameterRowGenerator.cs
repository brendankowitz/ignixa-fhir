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
/// Generates StringSearchParamListTableType DataTable rows from string search values.
/// String search parameters store text values with optional overflow for very long strings.
/// Supports min/max flags for sorting optimization.
/// </summary>
public class StringSearchParameterRowGenerator : ISearchParameterRowGenerator
{
    private const int StringColumnMaxLength = 128;

    public DataTable CreateDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("Text", typeof(string));
        table.Columns.Add("TextOverflow", typeof(string));
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

            // Extract all string search indices
            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not StringSearchValue stringValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Code, out var searchParamId))
                    continue;

                var row = table.NewRow();
                row["ResourceTypeId"] = resourceTypeId;
                row["ResourceSurrogateId"] = surrogateId;
                row["SearchParamId"] = searchParamId;

                // Handle text overflow for strings longer than column limit
                var textValue = stringValue.String;
                if (textValue != null && textValue.Length > StringColumnMaxLength)
                {
                    row["Text"] = textValue.Substring(0, StringColumnMaxLength);
                    row["TextOverflow"] = textValue.Substring(StringColumnMaxLength);
                }
                else
                {
                    row["Text"] = textValue ?? (object)DBNull.Value;
                    row["TextOverflow"] = DBNull.Value;
                }

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
