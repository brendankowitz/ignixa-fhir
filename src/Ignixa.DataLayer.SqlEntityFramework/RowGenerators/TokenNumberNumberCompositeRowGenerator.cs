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
            new SqlMetaData("SingleValue2", SqlDbType.Decimal),
            new SqlMetaData("LowValue2", SqlDbType.Decimal),
            new SqlMetaData("HighValue2", SqlDbType.Decimal),
            new SqlMetaData("SingleValue3", SqlDbType.Decimal),
            new SqlMetaData("LowValue3", SqlDbType.Decimal),
            new SqlMetaData("HighValue3", SqlDbType.Decimal),
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

                if (!searchParameterIdMap.TryGetValue(searchIndex.SearchParameter.Url.ToString(), out var searchParamId))
                    continue;

                foreach (var componentGroup in compositeValue.Components)
                {
                    TokenSearchValue? tokenComponent = null;
                    var numberComponents = new List<NumberSearchValue>();

                    foreach (var component in componentGroup)
                    {
                        if (component is TokenSearchValue tokenVal && tokenComponent == null)
                            tokenComponent = tokenVal;
                        else if (component is NumberSearchValue numberVal)
                            numberComponents.Add(numberVal);
                    }

                    if (tokenComponent == null || numberComponents.Count < 2)
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

                    // First number component (component 2)
                    var number1 = numberComponents[0];
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

                    // Second number component (component 3)
                    var number2 = numberComponents[1];
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

                    // Flag indicating if any component is a range (not a single value)
                    var hasRange = (number1.Low != number1.High) || (number2.Low != number2.High);
                    record.SetBoolean(12, hasRange);

                    yield return record;
                }
            }
        }
    }
}
