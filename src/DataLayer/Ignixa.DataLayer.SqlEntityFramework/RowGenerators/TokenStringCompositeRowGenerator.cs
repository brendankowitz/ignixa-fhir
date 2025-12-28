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
/// Generates TokenStringCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Handles Token|String composite combinations.
/// </summary>
public class TokenStringCompositeRowGenerator : ISearchParameterRowGenerator
{
    private const int StringColumnMaxLength = 128;
    private readonly IReadOnlyDictionary<string, int> _systemMappings;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenStringCompositeRowGenerator"/> class.
    /// </summary>
    /// <param name="systemMappings">Mapping of system URIs to their database IDs.</param>
    public TokenStringCompositeRowGenerator(IReadOnlyDictionary<string, int> systemMappings)
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
            new SqlMetaData("Text2", SqlDbType.NVarChar, 128),
            new SqlMetaData("TextOverflow2", SqlDbType.NVarChar, -1),
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

                var tokenComponents = compositeValue.Components[0].OfType<TokenSearchValue>().ToList();
                var stringComponents = compositeValue.Components[1].OfType<StringSearchValue>().ToList();

                if (tokenComponents.Count == 0 || stringComponents.Count == 0)
                    continue;

                foreach (var tokenComponent in tokenComponents)
                {
                    foreach (var stringComponent in stringComponents)
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
                            // System not found in cache - skip this record
                            continue;
                        }

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

                        // String component
                        var textValue = stringComponent.String?.ToUpperInvariant();
                        if (textValue != null && textValue.Length > StringColumnMaxLength)
                        {
                            record.SetString(6, textValue.Substring(0, StringColumnMaxLength));
                            record.SetString(7, textValue.Substring(StringColumnMaxLength));
                        }
                        else
                        {
                            if (textValue != null)
                                record.SetString(6, textValue);
                            else
                                record.SetDBNull(6);
                            record.SetDBNull(7);
                        }

                        yield return record;
                    }
                }
            }
        }
    }
}
