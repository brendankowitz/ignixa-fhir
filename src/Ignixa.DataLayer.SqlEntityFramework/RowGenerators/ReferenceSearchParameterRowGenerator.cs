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
/// Generates ReferenceSearchParamListTableType DataTable rows from reference search values.
/// Reference search parameters store references to other resources (e.g., "Patient/123").
/// </summary>
public class ReferenceSearchParameterRowGenerator : ISearchParameterRowGenerator
{
    public DataTable CreateDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("BaseUri", typeof(string));
        table.Columns.Add("ReferenceResourceTypeId", typeof(short));
        table.Columns.Add("ReferenceResourceId", typeof(string));
        table.Columns.Add("ReferenceResourceVersion", typeof(int));
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

            // Extract all reference search indices
            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not ReferenceSearchValue refValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Code, out var searchParamId))
                    continue;

                var row = table.NewRow();
                row["ResourceTypeId"] = resourceTypeId;
                row["ResourceSurrogateId"] = surrogateId;
                row["SearchParamId"] = searchParamId;

                // BaseUri is optional for local references
                row["BaseUri"] = refValue.BaseUri?.ToString() ?? (object)DBNull.Value;

                // ReferenceResourceTypeId lookup would be implemented in Phase 3
                if (!string.IsNullOrEmpty(refValue.ResourceType) && resourceTypeIdMap.TryGetValue(refValue.ResourceType, out var refResourceTypeId))
                {
                    row["ReferenceResourceTypeId"] = refResourceTypeId;
                }
                else
                {
                    row["ReferenceResourceTypeId"] = DBNull.Value;
                }

                row["ReferenceResourceId"] = refValue.ResourceId;

                // Version is optional
                row["ReferenceResourceVersion"] = DBNull.Value; // TODO Phase 3: Extract version if available

                table.Rows.Add(row);
            }
        }

        return table;
    }
}
