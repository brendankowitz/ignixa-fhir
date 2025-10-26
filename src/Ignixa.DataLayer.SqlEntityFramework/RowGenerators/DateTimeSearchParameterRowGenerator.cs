// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Microsoft.Data.SqlClient.Server;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Generates DateTimeSearchParamList TVP SqlDataRecord rows from datetime search values.
/// DateTime search parameters store date/time ranges with start and end times (always UTC).
/// Includes optimization for values longer than a day and min/max flags for sorting.
/// </summary>
public class DateTimeSearchParameterRowGenerator : ISearchParameterRowGenerator
{
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
            new SqlMetaData("StartDateTime", SqlDbType.DateTimeOffset),
            new SqlMetaData("EndDateTime", SqlDbType.DateTimeOffset),
            new SqlMetaData("IsLongerThanADay", SqlDbType.Bit),
            new SqlMetaData("IsMin", SqlDbType.Bit),
            new SqlMetaData("IsMax", SqlDbType.Bit),
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
                if (searchIndex.Value is not DateTimeSearchValue dateTimeValue)
                    continue;

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Url.ToString(), out var searchParamId))
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);
                record.SetDateTimeOffset(3, dateTimeValue.Start);
                record.SetDateTimeOffset(4, dateTimeValue.End);

                var duration = dateTimeValue.End - dateTimeValue.Start;
                record.SetBoolean(5, duration.TotalDays > 1);

                if (searchIndex.Value is ISupportSortSearchValue sortValue)
                {
                    record.SetBoolean(6, sortValue.IsMin);
                    record.SetBoolean(7, sortValue.IsMax);
                }
                else
                {
                    record.SetBoolean(6, false);
                    record.SetBoolean(7, false);
                }

                yield return record;
            }
        }
    }
}
