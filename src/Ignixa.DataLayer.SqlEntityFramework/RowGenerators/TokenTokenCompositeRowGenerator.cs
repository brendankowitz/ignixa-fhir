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
/// Generates TokenTokenCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Handles Token|Token composite combinations.
/// </summary>
public class TokenTokenCompositeRowGenerator : ISearchParameterRowGenerator
{
    public DataTable CreateDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("SystemId1", typeof(int));
        table.Columns.Add("Code1", typeof(string));
        table.Columns.Add("CodeOverflow1", typeof(string));
        table.Columns.Add("SystemId2", typeof(int));
        table.Columns.Add("Code2", typeof(string));
        table.Columns.Add("CodeOverflow2", typeof(string));
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

            // Extract all composite search indices with Token|Token components
            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not CompositeSearchValue compositeValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Code, out var searchParamId))
                    continue;

                // For each combination of components
                foreach (var componentGroup in compositeValue.Components)
                {
                    var tokenComponents = new List<TokenSearchValue>();

                    // Extract all Token components from this group
                    foreach (var component in componentGroup)
                    {
                        if (component is TokenSearchValue tokenVal)
                            tokenComponents.Add(tokenVal);
                    }

                    // Skip if we don't have exactly 2 token components
                    if (tokenComponents.Count != 2)
                        continue;

                    var row = table.NewRow();
                    row["ResourceTypeId"] = resourceTypeId;
                    row["ResourceSurrogateId"] = surrogateId;
                    row["SearchParamId"] = searchParamId;

                    // First token component
                    row["SystemId1"] = string.IsNullOrEmpty(tokenComponents[0].System) ? 0 : tokenComponents[0].System.GetHashCode(StringComparison.Ordinal);

                    if (tokenComponents[0].Code != null && tokenComponents[0].Code.Length > 128)
                    {
                        row["Code1"] = tokenComponents[0].Code.Substring(0, 128);
                        row["CodeOverflow1"] = tokenComponents[0].Code.Substring(128);
                    }
                    else
                    {
                        row["Code1"] = tokenComponents[0].Code ?? (object)DBNull.Value;
                        row["CodeOverflow1"] = DBNull.Value;
                    }

                    // Second token component
                    row["SystemId2"] = string.IsNullOrEmpty(tokenComponents[1].System) ? 0 : tokenComponents[1].System.GetHashCode(StringComparison.Ordinal);

                    if (tokenComponents[1].Code != null && tokenComponents[1].Code.Length > 128)
                    {
                        row["Code2"] = tokenComponents[1].Code.Substring(0, 128);
                        row["CodeOverflow2"] = tokenComponents[1].Code.Substring(128);
                    }
                    else
                    {
                        row["Code2"] = tokenComponents[1].Code ?? (object)DBNull.Value;
                        row["CodeOverflow2"] = DBNull.Value;
                    }

                    table.Rows.Add(row);
                }
            }
        }

        return table;
    }
}
