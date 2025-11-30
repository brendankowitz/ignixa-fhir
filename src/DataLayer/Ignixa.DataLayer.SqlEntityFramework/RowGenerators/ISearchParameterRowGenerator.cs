// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Microsoft.Data.SqlClient.Server;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Interface for generating TVP SqlDataRecord rows from search indices.
/// Implementations extract specific search parameter types from ResourceWrapper and stream
/// SqlDataRecord directly for bulk merge operations.
/// </summary>
public interface ISearchParameterRowGenerator
{
    /// <summary>
    /// Generates SqlDataRecord collection directly from search indices for efficient TVP streaming.
    /// </summary>
    /// <param name="resources">The resources to extract search parameters from.</param>
    /// <param name="resourceTypeIdMap">Mapping of resource type strings to their IDs.</param>
    /// <param name="searchParameterIdMap">Mapping of search parameter codes to their IDs.</param>
    /// <param name="resourceSurrogateIdMap">Mapping of resources to their allocated surrogate IDs (transactionId + index).</param>
    /// <returns>An enumerable of SqlDataRecord ready for TVP parameter binding.</returns>
    IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap);
}
