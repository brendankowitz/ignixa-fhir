// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Microsoft.Data.SqlClient.Server;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Generates TokenQuantityCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Handles Token|Quantity composite combinations.
/// </summary>
public class TokenQuantityCompositeRowGenerator : ISearchParameterRowGenerator
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
        table.Columns.Add("QuantityCodeId2", typeof(int));
        table.Columns.Add("SingleValue2", typeof(decimal));
        table.Columns.Add("LowValue2", typeof(decimal));
        table.Columns.Add("HighValue2", typeof(decimal));
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

            // Extract all composite search indices with Token|Quantity components
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
                    QuantitySearchValue? quantityComponent = null;

                    // Extract Token and Quantity components from this group
                    foreach (var component in componentGroup)
                    {
                        if (component is TokenSearchValue tokenVal && tokenComponent == null)
                            tokenComponent = tokenVal;
                        else if (component is QuantitySearchValue quantityVal && quantityComponent == null)
                            quantityComponent = quantityVal;
                    }

                    // Skip if we don't have both components
                    if (tokenComponent == null || quantityComponent == null)
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

                    // Quantity component (component 2)
                    row["SystemId2"] = string.IsNullOrEmpty(quantityComponent.System) ? 0 : quantityComponent.System.GetHashCode(StringComparison.Ordinal);
                    row["QuantityCodeId2"] = string.IsNullOrEmpty(quantityComponent.Code) ? 0 : quantityComponent.Code.GetHashCode(StringComparison.Ordinal);

                    if (quantityComponent.Low.HasValue && quantityComponent.High.HasValue && quantityComponent.Low == quantityComponent.High)
                    {
                        row["SingleValue2"] = quantityComponent.Low.Value;
                        row["LowValue2"] = DBNull.Value;
                        row["HighValue2"] = DBNull.Value;
                    }
                    else
                    {
                        row["SingleValue2"] = DBNull.Value;
                        row["LowValue2"] = quantityComponent.Low ?? (object)DBNull.Value;
                        row["HighValue2"] = quantityComponent.High ?? (object)DBNull.Value;
                    }

                    table.Rows.Add(row);
                }
            }
        }

        return table;
    }

    public IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap)
    {
        var metadata = new[]
        {
            new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
            new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
            new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
            new SqlMetaData("SystemId1", SqlDbType.Int),
            new SqlMetaData("Code1", SqlDbType.VarChar, 128),
            new SqlMetaData("CodeOverflow1", SqlDbType.VarChar, -1),
            new SqlMetaData("SystemId2", SqlDbType.Int),
            new SqlMetaData("QuantityCodeId2", SqlDbType.Int),
            new SqlMetaData("SingleValue2", SqlDbType.Decimal),
            new SqlMetaData("LowValue2", SqlDbType.Decimal),
            new SqlMetaData("HighValue2", SqlDbType.Decimal),
        };

        foreach (var resource in resources)
        {
            if (resource.SearchIndices == null || resource.SearchIndices.Count == 0)
                continue;

            if (!resourceTypeIdMap.TryGetValue(resource.ResourceType, out var resourceTypeId))
                continue;

            if (!resourceSurrogateIdMap.TryGetValue(resource, out var surrogateId))
                continue;

            foreach (var searchIndex in resource.SearchIndices.OfType<SearchIndexEntry>())
            {
                if (searchIndex.Value is not CompositeSearchValue compositeValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Code, out var searchParamId))
                    continue;

                foreach (var componentGroup in compositeValue.Components)
                {
                    TokenSearchValue? tokenComponent = null;
                    QuantitySearchValue? quantityComponent = null;

                    foreach (var component in componentGroup)
                    {
                        if (component is TokenSearchValue tokenVal && tokenComponent == null)
                            tokenComponent = tokenVal;
                        else if (component is QuantitySearchValue quantityVal && quantityComponent == null)
                            quantityComponent = quantityVal;
                    }

                    if (tokenComponent == null || quantityComponent == null)
                        continue;

                    var record = new SqlDataRecord(metadata);
                    record.SetInt16(0, resourceTypeId);
                    record.SetInt64(1, surrogateId);
                    record.SetInt16(2, searchParamId);

                    // Token component (component 1)
                    record.SetInt32(3, string.IsNullOrEmpty(tokenComponent.System) ? 0 : tokenComponent.System.GetHashCode(StringComparison.Ordinal));

                    if (tokenComponent.Code != null && tokenComponent.Code.Length > 128)
                    {
                        record.SetString(4, tokenComponent.Code.Substring(0, 128));
                        record.SetString(5, tokenComponent.Code.Substring(128));
                    }
                    else
                    {
                        if (tokenComponent.Code != null)
                            record.SetString(4, tokenComponent.Code);
                        else
                            record.SetDBNull(4);
                        record.SetDBNull(5);
                    }

                    // Quantity component (component 2)
                    record.SetInt32(6, string.IsNullOrEmpty(quantityComponent.System) ? 0 : quantityComponent.System.GetHashCode(StringComparison.Ordinal));
                    record.SetInt32(7, string.IsNullOrEmpty(quantityComponent.Code) ? 0 : quantityComponent.Code.GetHashCode(StringComparison.Ordinal));

                    if (quantityComponent.Low.HasValue && quantityComponent.High.HasValue && quantityComponent.Low == quantityComponent.High)
                    {
                        record.SetDecimal(8, quantityComponent.Low.Value);
                        record.SetDBNull(9);
                        record.SetDBNull(10);
                    }
                    else
                    {
                        record.SetDBNull(8);
                        if (quantityComponent.Low.HasValue)
                            record.SetDecimal(9, quantityComponent.Low.Value);
                        else
                            record.SetDBNull(9);
                        if (quantityComponent.High.HasValue)
                            record.SetDecimal(10, quantityComponent.High.Value);
                        else
                            record.SetDBNull(10);
                    }

                    yield return record;
                }
            }
        }
    }
}
