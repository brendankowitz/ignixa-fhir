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
/// Generates TokenStringCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Handles Token|String composite combinations.
/// </summary>
public class TokenStringCompositeRowGenerator : ISearchParameterRowGenerator
{
    private const int StringColumnMaxLength = 128;

    public DataTable CreateDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("SystemId1", typeof(int));
        table.Columns.Add("Code1", typeof(string));
        table.Columns.Add("CodeOverflow1", typeof(string));
        table.Columns.Add("Text2", typeof(string));
        table.Columns.Add("TextOverflow2", typeof(string));
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

            // Extract all composite search indices with Token|String components
            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not CompositeSearchValue compositeValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Code, out var searchParamId))
                    continue;

                // For each combination of components
                foreach (var componentGroup in compositeValue.Components)
                {
                    TokenSearchValue? tokenComponent = null;
                    StringSearchValue? stringComponent = null;

                    // Extract Token and String components from this group
                    foreach (var component in componentGroup)
                    {
                        if (component is TokenSearchValue tokenVal && tokenComponent == null)
                            tokenComponent = tokenVal;
                        else if (component is StringSearchValue stringVal && stringComponent == null)
                            stringComponent = stringVal;
                    }

                    // Skip if we don't have both components
                    if (tokenComponent == null || stringComponent == null)
                        continue;

                    var row = table.NewRow();
                    row["ResourceTypeId"] = resourceTypeId;
                    row["ResourceSurrogateId"] = surrogateId;
                    row["SearchParamId"] = searchParamId;

                    // Token component (component 1)
                    row["SystemId1"] = string.IsNullOrEmpty(tokenComponent.System) ? 0 : tokenComponent.System.GetHashCode(StringComparison.Ordinal);

                    if (tokenComponent.Code != null && tokenComponent.Code.Length > 128)
                    {
                        row["Code1"] = tokenComponent.Code.Substring(0, 128);
                        row["CodeOverflow1"] = tokenComponent.Code.Substring(128);
                    }
                    else
                    {
                        row["Code1"] = tokenComponent.Code ?? (object)DBNull.Value;
                        row["CodeOverflow1"] = DBNull.Value;
                    }

                    // String component (component 2)
                    var textValue = stringComponent.String;
                    if (textValue != null && textValue.Length > StringColumnMaxLength)
                    {
                        row["Text2"] = textValue.Substring(0, StringColumnMaxLength);
                        row["TextOverflow2"] = textValue.Substring(StringColumnMaxLength);
                    }
                    else
                    {
                        row["Text2"] = textValue ?? (object)DBNull.Value;
                        row["TextOverflow2"] = DBNull.Value;
                    }

                    table.Rows.Add(row);
                }
            }
        }

        return table;
    }
}
