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

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Url.ToString(), out var searchParamId))
                    continue;

                foreach (var componentGroup in compositeValue.Components)
                {
                    var tokenComponents = new List<TokenSearchValue>();

                    foreach (var component in componentGroup)
                    {
                        if (component is TokenSearchValue tokenVal)
                            tokenComponents.Add(tokenVal);
                    }

                    if (tokenComponents.Count != 2)
                        continue;

                    var record = new SqlDataRecord(metadata);
                    record.SetInt16(0, resourceTypeId);
                    record.SetInt64(1, surrogateId);
                    record.SetInt16(2, searchParamId);

                    // First token component
                    record.SetInt32(3, string.IsNullOrEmpty(tokenComponents[0].System) ? 0 : tokenComponents[0].System.GetHashCode(StringComparison.Ordinal));

                    if (tokenComponents[0].Code != null && tokenComponents[0].Code.Length > 128)
                    {
                        record.SetString(4, tokenComponents[0].Code.Substring(0, 128));
                        record.SetString(5, tokenComponents[0].Code.Substring(128));
                    }
                    else
                    {
                        if (tokenComponents[0].Code != null)
                            record.SetString(4, tokenComponents[0].Code);
                        else
                            record.SetDBNull(4);
                        record.SetDBNull(5);
                    }

                    // Second token component
                    record.SetInt32(6, string.IsNullOrEmpty(tokenComponents[1].System) ? 0 : tokenComponents[1].System.GetHashCode(StringComparison.Ordinal));

                    if (tokenComponents[1].Code != null && tokenComponents[1].Code.Length > 128)
                    {
                        record.SetString(7, tokenComponents[1].Code.Substring(0, 128));
                        record.SetString(8, tokenComponents[1].Code.Substring(128));
                    }
                    else
                    {
                        if (tokenComponents[1].Code != null)
                            record.SetString(7, tokenComponents[1].Code);
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
