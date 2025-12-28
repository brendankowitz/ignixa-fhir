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
/// Generates TokenTokenCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Handles Token|Token composite combinations.
/// </summary>
public class TokenTokenCompositeRowGenerator : ISearchParameterRowGenerator
{
    private readonly IReadOnlyDictionary<string, int> _systemMappings;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenTokenCompositeRowGenerator"/> class.
    /// </summary>
    /// <param name="systemMappings">Mapping of system URIs to their database IDs.</param>
    public TokenTokenCompositeRowGenerator(IReadOnlyDictionary<string, int> systemMappings)
    {
        _systemMappings = systemMappings ?? throw new ArgumentNullException(nameof(systemMappings));
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
            new SqlMetaData("Code2", SqlDbType.VarChar, 128),
            new SqlMetaData("CodeOverflow2", SqlDbType.VarChar, -1),
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

                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                    continue;

                if (compositeValue.Components.Count < 2)
                    continue;

                var tokenComponents1 = compositeValue.Components[0].OfType<TokenSearchValue>().ToList();
                var tokenComponents2 = compositeValue.Components[1].OfType<TokenSearchValue>().ToList();

                if (tokenComponents1.Count == 0 || tokenComponents2.Count == 0)
                    continue;

                foreach (var tokenComponent1 in tokenComponents1)
                {
                    foreach (var tokenComponent2 in tokenComponents2)
                    {
                        var record = new SqlDataRecord(metadata);
                        record.SetInt16(0, resourceTypeId);
                        record.SetInt64(1, surrogateId);
                        record.SetInt16(2, searchParamId);

                        // Token component 1 - use system mappings
                        if (string.IsNullOrEmpty(tokenComponent1.System))
                        {
                            record.SetDBNull(3);
                        }
                        else if (_systemMappings.TryGetValue(tokenComponent1.System, out var systemId1))
                        {
                            record.SetInt32(3, systemId1);
                        }
                        else
                        {
                            // System not found in cache - skip this record
                            continue;
                        }

                        if (tokenComponent1.Code != null && tokenComponent1.Code.Length > 128)
                        {
                            record.SetString(4, tokenComponent1.Code.Substring(0, 128));
                            record.SetString(5, tokenComponent1.Code.Substring(128));
                        }
                        else
                        {
                            if (tokenComponent1.Code != null)
                                record.SetString(4, tokenComponent1.Code);
                            else
                                record.SetDBNull(4);
                            record.SetDBNull(5);
                        }

                        // Token component 2 - use system mappings
                        if (string.IsNullOrEmpty(tokenComponent2.System))
                        {
                            record.SetDBNull(6);
                        }
                        else if (_systemMappings.TryGetValue(tokenComponent2.System, out var systemId2))
                        {
                            record.SetInt32(6, systemId2);
                        }
                        else
                        {
                            // System not found in cache - skip this record
                            continue;
                        }

                        if (tokenComponent2.Code != null && tokenComponent2.Code.Length > 128)
                        {
                            record.SetString(7, tokenComponent2.Code.Substring(0, 128));
                            record.SetString(8, tokenComponent2.Code.Substring(128));
                        }
                        else
                        {
                            if (tokenComponent2.Code != null)
                                record.SetString(7, tokenComponent2.Code);
                            else
                                record.SetDBNull(7);
                            record.SetDBNull(8);
                        }

                        yield return record;
                    }
                }
            }
        }
    }
}
