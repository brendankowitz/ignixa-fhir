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
    private readonly IReadOnlyDictionary<string, int> _systemMappings;
    private readonly IReadOnlyDictionary<string, int> _quantityCodeMappings;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenQuantityCompositeRowGenerator"/> class.
    /// </summary>
    /// <param name="systemMappings">Mapping of system URIs to their database IDs.</param>
    /// <param name="quantityCodeMappings">Mapping of quantity codes (units) to their database IDs.</param>
    public TokenQuantityCompositeRowGenerator(
        IReadOnlyDictionary<string, int> systemMappings,
        IReadOnlyDictionary<string, int> quantityCodeMappings)
    {
        _systemMappings = systemMappings ?? throw new ArgumentNullException(nameof(systemMappings));
        _quantityCodeMappings = quantityCodeMappings ?? throw new ArgumentNullException(nameof(quantityCodeMappings));
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
            new SqlMetaData("SingleValue2", SqlDbType.Decimal, precision: 36, scale: 18),
            new SqlMetaData("LowValue2", SqlDbType.Decimal, precision: 36, scale: 18),
            new SqlMetaData("HighValue2", SqlDbType.Decimal, precision: 36, scale: 18),
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
                var quantityComponents = compositeValue.Components[1].OfType<QuantitySearchValue>().ToList();

                if (tokenComponents.Count == 0 || quantityComponents.Count == 0)
                    continue;

                foreach (var tokenComponent in tokenComponents)
                {
                    foreach (var quantityComponent in quantityComponents)
                    {
                        var record = new SqlDataRecord(metadata);
                        record.SetInt16(0, resourceTypeId);
                        record.SetInt64(1, surrogateId);
                        record.SetInt16(2, searchParamId);

                        // Token component (component 1) - use system mappings
                        if (string.IsNullOrEmpty(tokenComponent.System))
                        {
                            record.SetDBNull(3);
                        }
                        else if (_systemMappings.TryGetValue(tokenComponent.System, out var tokenSystemId))
                        {
                            record.SetInt32(3, tokenSystemId);
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

                        // Quantity component (component 2) - use system and quantity code mappings
                        if (string.IsNullOrEmpty(quantityComponent.System))
                        {
                            record.SetDBNull(6);
                        }
                        else if (_systemMappings.TryGetValue(quantityComponent.System, out var quantitySystemId))
                        {
                            record.SetInt32(6, quantitySystemId);
                        }
                        else
                        {
                            // System not found in cache - skip this record
                            continue;
                        }

                        if (string.IsNullOrEmpty(quantityComponent.Code))
                        {
                            record.SetDBNull(7);
                        }
                        else if (_quantityCodeMappings.TryGetValue(quantityComponent.Code, out var quantityCodeId))
                        {
                            record.SetInt32(7, quantityCodeId);
                        }
                        else
                        {
                            // Quantity code not found in cache - skip this record
                            continue;
                        }

                        // Quantity value columns:
                        // Column 8: SingleValue2 - exact match optimization (nullable)
                        // Column 9: LowValue2 - lower bound for range queries (NOT NULL, use sentinel for unbounded)
                        // Column 10: HighValue2 - upper bound for range queries (NOT NULL, use sentinel for unbounded)
                        // Pattern: For exact matches, store value in SingleValue2 AND duplicate to Low/High
                        const decimal DefaultLowValue = -999999999999999999m;
                        const decimal DefaultHighValue = 999999999999999999m;

                        if (quantityComponent.Low.HasValue && quantityComponent.High.HasValue && quantityComponent.Low == quantityComponent.High)
                        {
                            // Exact match: store in SingleValue2 AND duplicate to Low/High
                            var value = quantityComponent.Low.Value;
                            record.SetDecimal(8, value);
                            record.SetDecimal(9, value);
                            record.SetDecimal(10, value);
                        }
                        else
                        {
                            // Range: SingleValue2 is null, Low/High use defaults if unbounded
                            record.SetDBNull(8);
                            record.SetDecimal(9, quantityComponent.Low ?? DefaultLowValue);
                            record.SetDecimal(10, quantityComponent.High ?? DefaultHighValue);
                        }

                        yield return record;
                    }
                }
            }
        }

    }
}
