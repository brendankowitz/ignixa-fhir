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
/// Generates TokenSearchParamListTableType DataTable rows from token search values.
/// Token search parameters use a System|Code format (e.g., "http://system|code").
/// </summary>
public class TokenSearchParameterRowGenerator : ISearchParameterRowGenerator
{
    public DataTable CreateDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("SystemId", typeof(int));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("CodeOverflow", typeof(string));
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

            // Look up the allocated surrogate ID for this resource
            if (!resourceSurrogateIdMap.TryGetValue(resource, out var surrogateId))
                continue; // Skip if not found (shouldn't happen)

            // Extract all token search indices
            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not TokenSearchValue tokenValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Code, out var searchParamId))
                    continue;

                var row = table.NewRow();
                row["ResourceTypeId"] = resourceTypeId;
                row["ResourceSurrogateId"] = surrogateId;
                row["SearchParamId"] = searchParamId;

                // SystemId lookup would be implemented in Phase 3
                // For now, use hash of system string as placeholder
                row["SystemId"] = string.IsNullOrEmpty(tokenValue.System) ? 0 : tokenValue.System.GetHashCode(StringComparison.Ordinal);

                // Handle code overflow for very long codes
                if (tokenValue.Code != null && tokenValue.Code.Length > 128)
                {
                    row["Code"] = tokenValue.Code.Substring(0, 128);
                    row["CodeOverflow"] = tokenValue.Code.Substring(128);
                }
                else
                {
                    row["Code"] = tokenValue.Code ?? (object)DBNull.Value;
                    row["CodeOverflow"] = DBNull.Value;
                }

                table.Rows.Add(row);
            }
        }

        return table;
    }
}
