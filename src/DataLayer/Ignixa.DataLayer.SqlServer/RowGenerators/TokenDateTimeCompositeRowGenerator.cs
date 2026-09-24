// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.RowGenerators;

/// <summary>
/// Generates TokenDateTimeCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Handles Token|DateTime composite combinations.
/// </summary>
public class TokenDateTimeCompositeRowGenerator : ISearchParameterRowGenerator
{
    private static readonly int Code1Width = SearchParamColumnWidths.For("TokenDateTimeCompositeSearchParam", "Code1");

    private readonly IReadOnlyDictionary<string, int> _systemMappings;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenDateTimeCompositeRowGenerator"/> class.
    /// </summary>
    /// <param name="systemMappings">Mapping of system URIs to their database IDs.</param>
    public TokenDateTimeCompositeRowGenerator(IReadOnlyDictionary<string, int> systemMappings)
    {
        _systemMappings = systemMappings ?? throw new ArgumentNullException(nameof(systemMappings));
    }

    public IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap,
        ILogger logger)
    {
        var metadata = new[]
        {
            new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
            new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
            new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
            new SqlMetaData("SystemId1", SqlDbType.Int),
            new SqlMetaData("Code1", SqlDbType.VarChar, Code1Width),
            new SqlMetaData("CodeOverflow1", SqlDbType.VarChar, -1),
            new SqlMetaData("StartDateTime2", SqlDbType.DateTimeOffset),
            new SqlMetaData("EndDateTime2", SqlDbType.DateTimeOffset),
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
                if (searchIndex.Value is not CompositeIndexSearchValue compositeValue)
                    continue;

                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                {
                    logger.LogWarning(
                        "SearchParamId not found in cache for {SearchParameterUrl} while indexing {ResourceType}/{ResourceId} -- row skipped",
                        searchIndex.SearchParameter.Url, resource.ResourceType, resource.ResourceId);
                    continue;
                }

                if (compositeValue.Components.Count < 2)
                    continue;

                var tokenComponents = compositeValue.Components[0].OfType<TokenSearchValue>().ToList();
                var dateTimeComponents = compositeValue.Components[1].OfType<DateTimeSearchValue>().ToList();

                if (tokenComponents.Count == 0 || dateTimeComponents.Count == 0)
                    continue;

                foreach (var tokenComponent in tokenComponents)
                {
                    // Code1 is NOT NULL in the TVP schema, so a text-only token (no coding) has nothing
                    // to index here and the whole composite row is skipped rather than written as NULL
                    if (string.IsNullOrEmpty(tokenComponent.Code))
                        continue;

                    foreach (var dateTimeComponent in dateTimeComponents)
                    {
                        var record = new SqlDataRecord(metadata);
                        record.SetInt16(0, resourceTypeId);
                        record.SetInt64(1, surrogateId);
                        record.SetInt16(2, searchParamId);

                        // Token component - use system mappings
                        if (string.IsNullOrEmpty(tokenComponent.System))
                        {
                            record.SetDBNull(3);
                        }
                        else if (_systemMappings.TryGetValue(tokenComponent.System, out var systemId))
                        {
                            record.SetInt32(3, systemId);
                        }
                        else
                        {
                            logger.LogWarning(
                                "SystemId not found in cache for {System} on {SearchParameterUrl} while indexing {ResourceType}/{ResourceId} -- row skipped",
                                tokenComponent.System, searchIndex.SearchParameter.Url, resource.ResourceType, resource.ResourceId);
                            continue;
                        }

                        if (tokenComponent.Code.Length > Code1Width)
                        {
                            record.SetString(4, tokenComponent.Code.Substring(0, Code1Width));
                            record.SetString(5, tokenComponent.Code.Substring(Code1Width));
                        }
                        else
                        {
                            record.SetString(4, tokenComponent.Code);
                            record.SetDBNull(5);
                        }

                        // DateTime component -- DATETIMEOFFSET(7) columns, matching the leaf DateTimeSearchParam
                        record.SetDateTimeOffset(6, dateTimeComponent.Start);
                        record.SetDateTimeOffset(7, dateTimeComponent.End);

                        var duration = dateTimeComponent.End - dateTimeComponent.Start;
                        record.SetBoolean(8, duration.TotalDays > 1);

                        yield return record;
                    }
                }
            }
        }
    }
}
