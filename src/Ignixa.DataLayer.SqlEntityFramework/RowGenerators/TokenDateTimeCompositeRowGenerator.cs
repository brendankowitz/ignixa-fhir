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
/// Generates TokenDateTimeCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Handles Token|DateTime composite combinations.
/// </summary>
public class TokenDateTimeCompositeRowGenerator : ISearchParameterRowGenerator
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
        table.Columns.Add("StartDateTime2", typeof(DateTime));
        table.Columns.Add("EndDateTime2", typeof(DateTime));
        table.Columns.Add("IsLongerThanADay2", typeof(bool));
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

            // Extract all composite search indices with Token|DateTime components
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
                    DateTimeSearchValue? dateTimeComponent = null;

                    // Extract Token and DateTime components from this group
                    foreach (var component in componentGroup)
                    {
                        if (component is TokenSearchValue tokenVal && tokenComponent == null)
                            tokenComponent = tokenVal;
                        else if (component is DateTimeSearchValue dateTimeVal && dateTimeComponent == null)
                            dateTimeComponent = dateTimeVal;
                    }

                    // Skip if we don't have both components
                    if (tokenComponent == null || dateTimeComponent == null)
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

                    // DateTime component (component 2)
                    row["StartDateTime2"] = dateTimeComponent.Start.UtcDateTime;
                    row["EndDateTime2"] = dateTimeComponent.End.UtcDateTime;

                    var duration = dateTimeComponent.End - dateTimeComponent.Start;
                    row["IsLongerThanADay2"] = duration.TotalDays > 1;

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
            new SqlMetaData("StartDateTime2", SqlDbType.DateTime),
            new SqlMetaData("EndDateTime2", SqlDbType.DateTime),
            new SqlMetaData("IsLongerThanADay2", SqlDbType.Bit),
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
                    DateTimeSearchValue? dateTimeComponent = null;

                    foreach (var component in componentGroup)
                    {
                        if (component is TokenSearchValue tokenVal && tokenComponent == null)
                            tokenComponent = tokenVal;
                        else if (component is DateTimeSearchValue dateTimeVal && dateTimeComponent == null)
                            dateTimeComponent = dateTimeVal;
                    }

                    if (tokenComponent == null || dateTimeComponent == null)
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

                    // DateTime component (component 2)
                    record.SetDateTime(6, dateTimeComponent.Start.UtcDateTime);
                    record.SetDateTime(7, dateTimeComponent.End.UtcDateTime);

                    var duration = dateTimeComponent.End - dateTimeComponent.Start;
                    record.SetBoolean(8, duration.TotalDays > 1);

                    yield return record;
                }
            }
        }
    }
}
