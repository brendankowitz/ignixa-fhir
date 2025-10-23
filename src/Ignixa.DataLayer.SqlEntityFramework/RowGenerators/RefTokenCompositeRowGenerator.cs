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
/// Generates ReferenceTokenCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Composite searches combine two search values (e.g., Reference + Token) into a single index entry.
/// This generator handles Reference|Token combinations.
/// </summary>
public class RefTokenCompositeRowGenerator : ISearchParameterRowGenerator
{
    public DataTable CreateDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("BaseUri1", typeof(string));
        table.Columns.Add("ReferenceResourceTypeId1", typeof(short));
        table.Columns.Add("ReferenceResourceId1", typeof(string));
        table.Columns.Add("ReferenceResourceVersion1", typeof(int));
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

            // Extract all composite search indices with Reference|Token components
            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not CompositeSearchValue compositeValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Code, out var searchParamId))
                    continue;

                // For each combination of components
                foreach (var componentGroup in compositeValue.Components)
                {
                    ReferenceSearchValue? refComponent = null;
                    TokenSearchValue? tokenComponent = null;

                    // Extract Reference and Token components from this group
                    foreach (var component in componentGroup)
                    {
                        if (component is ReferenceSearchValue refVal && refComponent == null)
                            refComponent = refVal;
                        else if (component is TokenSearchValue tokenVal && tokenComponent == null)
                            tokenComponent = tokenVal;
                    }

                    // Skip if we don't have both components
                    if (refComponent == null || tokenComponent == null)
                        continue;

                    var row = table.NewRow();
                    row["ResourceTypeId"] = resourceTypeId;
                    row["ResourceSurrogateId"] = surrogateId;
                    row["SearchParamId"] = searchParamId;

                    // Reference component (component 1)
                    row["BaseUri1"] = refComponent.BaseUri?.ToString() ?? (object)DBNull.Value;

                    if (!string.IsNullOrEmpty(refComponent.ResourceType) && 
                        resourceTypeIdMap.TryGetValue(refComponent.ResourceType, out var refResourceTypeId))
                    {
                        row["ReferenceResourceTypeId1"] = refResourceTypeId;
                    }
                    else
                    {
                        row["ReferenceResourceTypeId1"] = DBNull.Value;
                    }

                    row["ReferenceResourceId1"] = refComponent.ResourceId;
                    row["ReferenceResourceVersion1"] = DBNull.Value; // TODO Phase 3: Extract version if available

                    // Token component (component 2)
                    row["SystemId2"] = string.IsNullOrEmpty(tokenComponent.System) ? 0 : tokenComponent.System.GetHashCode(StringComparison.Ordinal);

                    if (tokenComponent.Code != null && tokenComponent.Code.Length > 128)
                    {
                        row["Code2"] = tokenComponent.Code.Substring(0, 128);
                        row["CodeOverflow2"] = tokenComponent.Code.Substring(128);
                    }
                    else
                    {
                        row["Code2"] = tokenComponent.Code ?? (object)DBNull.Value;
                        row["CodeOverflow2"] = DBNull.Value;
                    }

                    table.Rows.Add(row);
                }
            }
        }

        return table;
    }
}
