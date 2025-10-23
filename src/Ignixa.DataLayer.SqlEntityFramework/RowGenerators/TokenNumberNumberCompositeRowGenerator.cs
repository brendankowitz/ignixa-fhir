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
/// Generates TokenNumberNumberCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Handles Token|Number|Number composite combinations.
/// This is the most complex composite as it involves three components.
/// </summary>
public class TokenNumberNumberCompositeRowGenerator : ISearchParameterRowGenerator
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
        table.Columns.Add("SingleValue2", typeof(decimal));
        table.Columns.Add("LowValue2", typeof(decimal));
        table.Columns.Add("HighValue2", typeof(decimal));
        table.Columns.Add("SingleValue3", typeof(decimal));
        table.Columns.Add("LowValue3", typeof(decimal));
        table.Columns.Add("HighValue3", typeof(decimal));
        table.Columns.Add("HasRange", typeof(bool));
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

            // Extract all composite search indices with Token|Number|Number components
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
                    var numberComponents = new List<NumberSearchValue>();

                    // Extract Token and Number components from this group
                    foreach (var component in componentGroup)
                    {
                        if (component is TokenSearchValue tokenVal && tokenComponent == null)
                            tokenComponent = tokenVal;
                        else if (component is NumberSearchValue numberVal)
                            numberComponents.Add(numberVal);
                    }

                    // Skip if we don't have token and at least 2 numbers
                    if (tokenComponent == null || numberComponents.Count < 2)
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

                    // First number component (component 2)
                    var number1 = numberComponents[0];
                    if (number1.Low.HasValue && number1.High.HasValue && number1.Low == number1.High)
                    {
                        row["SingleValue2"] = number1.Low.Value;
                        row["LowValue2"] = DBNull.Value;
                        row["HighValue2"] = DBNull.Value;
                    }
                    else
                    {
                        row["SingleValue2"] = DBNull.Value;
                        row["LowValue2"] = number1.Low ?? (object)DBNull.Value;
                        row["HighValue2"] = number1.High ?? (object)DBNull.Value;
                    }

                    // Second number component (component 3)
                    var number2 = numberComponents[1];
                    if (number2.Low.HasValue && number2.High.HasValue && number2.Low == number2.High)
                    {
                        row["SingleValue3"] = number2.Low.Value;
                        row["LowValue3"] = DBNull.Value;
                        row["HighValue3"] = DBNull.Value;
                    }
                    else
                    {
                        row["SingleValue3"] = DBNull.Value;
                        row["LowValue3"] = number2.Low ?? (object)DBNull.Value;
                        row["HighValue3"] = number2.High ?? (object)DBNull.Value;
                    }

                    // Flag indicating if any component is a range (not a single value)
                    var hasRange = (number1.Low != number1.High) || (number2.Low != number2.High);
                    row["HasRange"] = hasRange;

                    table.Rows.Add(row);
                }
            }
        }

        return table;
    }
}
