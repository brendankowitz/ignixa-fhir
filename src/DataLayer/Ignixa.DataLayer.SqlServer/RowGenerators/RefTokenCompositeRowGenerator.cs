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
/// Generates ReferenceTokenCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Composite searches combine two search values (e.g., Reference + Token) into a single index entry.
/// This generator handles Reference|Token combinations.
/// </summary>
public class RefTokenCompositeRowGenerator : ISearchParameterRowGenerator
{
    private static readonly int Code2Width = SearchParamColumnWidths.For("ReferenceTokenCompositeSearchParam", "Code2");

    private readonly IReadOnlyDictionary<string, int> _systemMappings;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefTokenCompositeRowGenerator"/> class.
    /// </summary>
    /// <param name="systemMappings">Mapping of system URIs to their database IDs.</param>
    public RefTokenCompositeRowGenerator(IReadOnlyDictionary<string, int> systemMappings)
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
            new SqlMetaData("BaseUri1", SqlDbType.VarChar, 128),
            new SqlMetaData("ReferenceResourceTypeId1", SqlDbType.SmallInt),
            new SqlMetaData("ReferenceResourceId1", SqlDbType.VarChar, 64),
            new SqlMetaData("ReferenceResourceVersion1", SqlDbType.Int),
            new SqlMetaData("SystemId2", SqlDbType.Int),
            new SqlMetaData("Code2", SqlDbType.VarChar, Code2Width),
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

                // Find Reference and Token components by type, not by position
                // This handles cases where component definitions have swapped expressions
                var refComponents = new List<ReferenceSearchValue>();
                var tokenComponents = new List<TokenSearchValue>();

                foreach (var component in compositeValue.Components)
                {
                    refComponents.AddRange(component.OfType<ReferenceSearchValue>());
                    tokenComponents.AddRange(component.OfType<TokenSearchValue>());
                }

                if (refComponents.Count == 0 || tokenComponents.Count == 0)
                    continue;

                foreach (var refComponent in refComponents)
                {
                    // ReferenceResourceId1 is NOT NULL in the TVP schema, so a reference component with no
                    // resolvable resource id has nothing to index here and the whole composite row is
                    // skipped rather than written as NULL -- same reasoning as the Code2 check below
                    if (string.IsNullOrEmpty(refComponent.ResourceId))
                    {
                        logger.LogWarning(
                            "ReferenceResourceId missing on {SearchParameterUrl} while indexing {ResourceType}/{ResourceId} -- row skipped",
                            searchIndex.SearchParameter.Url, resource.ResourceType, resource.ResourceId);
                        continue;
                    }

                    foreach (var tokenComponent in tokenComponents)
                    {
                        // Code2 is NOT NULL in the TVP schema, so a text-only token (no coding) has nothing
                        // to index here and the whole composite row is skipped rather than written as NULL
                        if (string.IsNullOrEmpty(tokenComponent.Code))
                            continue;

                        var record = new SqlDataRecord(metadata);
                        record.SetInt16(0, resourceTypeId);
                        record.SetInt64(1, surrogateId);
                        record.SetInt16(2, searchParamId);

                        // Reference component
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
                        record.SetDBNull(6);

                        // Token component - use system mappings
                        if (string.IsNullOrEmpty(tokenComponent.System))
                        {
                            record.SetDBNull(7);
                        }
                        else if (_systemMappings.TryGetValue(tokenComponent.System, out var systemId))
                        {
                            record.SetInt32(7, systemId);
                        }
                        else
                        {
                            logger.LogWarning(
                                "SystemId not found in cache for {System} on {SearchParameterUrl} while indexing {ResourceType}/{ResourceId} -- row skipped",
                                tokenComponent.System, searchIndex.SearchParameter.Url, resource.ResourceType, resource.ResourceId);
                            continue;
                        }

                        if (tokenComponent.Code.Length > Code2Width)
                        {
                            record.SetString(8, tokenComponent.Code.Substring(0, Code2Width));
                            record.SetString(9, tokenComponent.Code.Substring(Code2Width));
                        }
                        else
                        {
                            record.SetString(8, tokenComponent.Code);
                            record.SetDBNull(9);
                        }

                        yield return record;
                    }
                }
            }
        }
    }
}
