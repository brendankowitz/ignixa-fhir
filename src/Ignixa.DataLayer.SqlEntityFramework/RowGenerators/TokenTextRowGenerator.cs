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
/// Generates TokenTextListTableType DataTable rows for token search value display text.
/// Token text is the human-readable display text associated with token search parameters
/// (e.g., the display property of a code system or concept).
/// </summary>
public class TokenTextRowGenerator : ISearchParameterRowGenerator
{
    public DataTable CreateDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("Text", typeof(string));
        table.Columns.Add("TextOverflow", typeof(string));
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

            // Extract all token search indices with display text
            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not TokenSearchValue tokenValue)
                    continue;

                // Only create token text entry if display text is present
                if (string.IsNullOrEmpty(tokenValue.Text))
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Code, out var searchParamId))
                    continue;

                var row = table.NewRow();
                row["ResourceSurrogateId"] = surrogateId;
                row["SearchParamId"] = searchParamId;

                // Split text if longer than 256 characters
                if (tokenValue.Text!.Length <= 256)
                {
                    row["Text"] = tokenValue.Text;
                    row["TextOverflow"] = DBNull.Value;
                }
                else
                {
                    row["Text"] = tokenValue.Text.Substring(0, 256);
                    row["TextOverflow"] = tokenValue.Text;
                }

                table.Rows.Add(row);
            }
        }

        return table;
    }
}
