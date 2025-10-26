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

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Url.ToString(), out var searchParamId))
                    continue;

                foreach (var componentGroup in compositeValue.Components)
                {
                    TokenSearchValue? tokenComponent = null;
                    StringSearchValue? stringComponent = null;

                    foreach (var component in componentGroup)
                    {
                        if (component is TokenSearchValue tokenVal && tokenComponent == null)
                            tokenComponent = tokenVal;
                        else if (component is StringSearchValue stringVal && stringComponent == null)
                            stringComponent = stringVal;
                    }

                    if (tokenComponent == null || stringComponent == null)
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

                    // String component (component 2)
                    var textValue = stringComponent.String;
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
