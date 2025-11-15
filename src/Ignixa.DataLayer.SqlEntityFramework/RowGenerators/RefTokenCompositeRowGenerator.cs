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
/// Generates ReferenceTokenCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Composite searches combine two search values (e.g., Reference + Token) into a single index entry.
/// This generator handles Reference|Token combinations.
/// </summary>
public class RefTokenCompositeRowGenerator : ISearchParameterRowGenerator
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
            new SqlMetaData("BaseUri1", SqlDbType.VarChar, 128),
            new SqlMetaData("ReferenceResourceTypeId1", SqlDbType.SmallInt),
            new SqlMetaData("ReferenceResourceId1", SqlDbType.VarChar, 64),
            new SqlMetaData("ReferenceResourceVersion1", SqlDbType.Int),
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

                foreach (var componentGroup in compositeValue.Components)
                {
                    ReferenceSearchValue? refComponent = null;
                    TokenSearchValue? tokenComponent = null;

                    foreach (var component in componentGroup)
                    {
                        if (component is ReferenceSearchValue refVal && refComponent == null)
                            refComponent = refVal;
                        else if (component is TokenSearchValue tokenVal && tokenComponent == null)
                            tokenComponent = tokenVal;
                    }

                    if (refComponent == null || tokenComponent == null)
                        continue;

                    var record = new SqlDataRecord(metadata);
                    record.SetInt16(0, resourceTypeId);
                    record.SetInt64(1, surrogateId);
                    record.SetInt16(2, searchParamId);

                    // Reference component (component 1)
                    if (refComponent.BaseUri != null)
                        record.SetString(3, refComponent.BaseUri.ToString());
                    else
                        record.SetDBNull(3);

                    if (!string.IsNullOrEmpty(refComponent.ResourceType) &&
                        resourceTypeIdMap.TryGetValue(refComponent.ResourceType, out var refResourceTypeId))
                    {
                        record.SetInt16(4, refResourceTypeId);
                    }
                    else
                    {
                        record.SetDBNull(4);
                    }

                    record.SetString(5, refComponent.ResourceId);
                    record.SetDBNull(6); // TODO Phase 3: Extract version if available

                    // Token component (component 2)
                    record.SetInt32(7, string.IsNullOrEmpty(tokenComponent.System) ? 0 : tokenComponent.System.GetHashCode(StringComparison.Ordinal));

                    if (tokenComponent.Code != null && tokenComponent.Code.Length > 128)
                    {
                        record.SetString(8, tokenComponent.Code.Substring(0, 128));
                        record.SetString(9, tokenComponent.Code.Substring(128));
                    }
                    else
                    {
                        if (tokenComponent.Code != null)
                            record.SetString(8, tokenComponent.Code);
                        else
                            record.SetDBNull(8);
                        record.SetDBNull(9);
                    }

                    yield return record;
                }
            }
        }
    }
}
