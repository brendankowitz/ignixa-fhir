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
/// Generates TokenNumberNumberCompositeSearchParamListTableType DataTable rows for composite search parameters.
/// Handles Token|Number|Number composite combinations.
/// This is the most complex composite as it involves three components.
/// </summary>
public class TokenNumberNumberCompositeRowGenerator : ISearchParameterRowGenerator
{
    private readonly IReadOnlyDictionary<string, int> _systemMappings;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenNumberNumberCompositeRowGenerator"/> class.
    /// </summary>
    /// <param name="systemMappings">Mapping of system URIs to their database IDs.</param>
    public TokenNumberNumberCompositeRowGenerator(IReadOnlyDictionary<string, int> systemMappings)
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
            new SqlMetaData("SingleValue2", SqlDbType.Decimal, precision: 36, scale: 18),
            new SqlMetaData("LowValue2", SqlDbType.Decimal, precision: 36, scale: 18),
            new SqlMetaData("HighValue2", SqlDbType.Decimal, precision: 36, scale: 18),
            new SqlMetaData("SingleValue3", SqlDbType.Decimal, precision: 36, scale: 18),
            new SqlMetaData("LowValue3", SqlDbType.Decimal, precision: 36, scale: 18),
            new SqlMetaData("HighValue3", SqlDbType.Decimal, precision: 36, scale: 18),
            new SqlMetaData("HasRange", SqlDbType.Bit),
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

                if (compositeValue.Components.Count < 3)
                    continue;

                var tokenComponents = compositeValue.Components[0].OfType<TokenSearchValue>().ToList();
                var numberComponents1 = compositeValue.Components[1].OfType<NumberSearchValue>().ToList();
                var numberComponents2 = compositeValue.Components[2].OfType<NumberSearchValue>().ToList();

                if (tokenComponents.Count == 0 || numberComponents1.Count == 0 || numberComponents2.Count == 0)
                    continue;

                foreach (var tokenComponent in tokenComponents)
                {
                    foreach (var number1 in numberComponents1)
                    {
                        foreach (var number2 in numberComponents2)
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

                            // Number component 1
                            if (number1.Low.HasValue && number1.High.HasValue && number1.Low == number1.High)
                            {
                                record.SetDecimal(6, number1.Low.Value);
                                record.SetDBNull(7);
                                record.SetDBNull(8);
                            }
                            else
                            {
                                record.SetDBNull(6);
                                if (number1.Low.HasValue)
                                    record.SetDecimal(7, number1.Low.Value);
                                else
                                    record.SetDBNull(7);
                                if (number1.High.HasValue)
                                    record.SetDecimal(8, number1.High.Value);
                                else
                                    record.SetDBNull(8);
                            }

                            // Number component 2
                            if (number2.Low.HasValue && number2.High.HasValue && number2.Low == number2.High)
                            {
                                record.SetDecimal(9, number2.Low.Value);
                                record.SetDBNull(10);
                                record.SetDBNull(11);
                            }
                            else
                            {
                                record.SetDBNull(9);
                                if (number2.Low.HasValue)
                                    record.SetDecimal(10, number2.Low.Value);
                                else
                                    record.SetDBNull(10);
                                if (number2.High.HasValue)
                                    record.SetDecimal(11, number2.High.Value);
                                else
                                    record.SetDBNull(11);
                            }

                            var hasRange = (number1.Low != number1.High) || (number2.Low != number2.High);
                            record.SetBoolean(12, hasRange);

                            yield return record;
                        }
                    }
                }
            }
        }
    }
}
