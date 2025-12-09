// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Microsoft.Data.SqlClient.Server;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Generates StringSearchParamList TVP SqlDataRecord rows from string search values.
/// String search parameters store text values with optional overflow for very long strings.
/// Supports min/max flags for sorting optimization.
/// </summary>
/// <remarks>
/// Text storage strategy for FHIR string search:
/// - Text column stores first 256 chars of ORIGINAL case text (for :exact modifier support)
/// - TextOverflow stores remaining chars for strings longer than 256 chars
/// - Query-time collation is used for case-sensitivity:
///   - No modifier / :contains: Latin1_General_100_CI_AI (case-insensitive, accent-insensitive)
///   - :exact: Latin1_General_100_CS_AS (case-sensitive, accent-sensitive)
///
/// This design enables proper :exact modifier support while maintaining efficient
/// case-insensitive search via SQL collation functions at query time.
/// </remarks>
public class StringSearchParameterRowGenerator : ISearchParameterRowGenerator
{
    // Text column max length matches the database column definition (256 chars)
    // TextOverflow (nvarchar(max)) handles any additional characters
    private const int StringColumnMaxLength = 256;

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
            new SqlMetaData("Text", SqlDbType.NVarChar, 256),
            new SqlMetaData("TextOverflow", SqlDbType.NVarChar, -1),
            new SqlMetaData("IsMin", SqlDbType.Bit),
            new SqlMetaData("IsMax", SqlDbType.Bit),
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
                if (searchIndex.Value is not StringSearchValue stringValue)
                    continue;

                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                    continue;

                var record = new SqlDataRecord(metadata);
                record.SetInt16(0, resourceTypeId);
                record.SetInt64(1, surrogateId);
                record.SetInt16(2, searchParamId);

                // Store text in ORIGINAL case for :exact modifier support
                // Case-insensitive search is handled via query-time collation (Latin1_General_100_CI_AI)
                var textValue = stringValue.String;
                if (textValue != null && textValue.Length > StringColumnMaxLength)
                {
                    record.SetString(3, textValue.Substring(0, StringColumnMaxLength));
                    record.SetString(4, textValue.Substring(StringColumnMaxLength));
                }
                else
                {
                    if (textValue != null)
                        record.SetString(3, textValue);
                    else
                        record.SetDBNull(3);
                    record.SetDBNull(4);
                }

                if (searchIndex.Value is ISupportSortSearchValue sortValue)
                {
                    record.SetBoolean(5, sortValue.IsMin);
                    record.SetBoolean(6, sortValue.IsMax);
                }
                else
                {
                    record.SetBoolean(5, false);
                    record.SetBoolean(6, false);
                }

                yield return record;
            }
        }
    }
}
