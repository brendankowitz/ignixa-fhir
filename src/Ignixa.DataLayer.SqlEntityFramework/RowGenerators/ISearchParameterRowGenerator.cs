// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Interface for generating TVP DataTable rows from search indices.
/// Implementations extract specific search parameter types from ResourceWrapper and populate
/// TVP DataTables for bulk merge operations.
/// </summary>
public interface ISearchParameterRowGenerator
{
    /// <summary>
    /// Creates an empty DataTable with the correct column structure for this TVP type.
    /// This ensures column definitions are centralized and consistent between row generators and merge repository.
    /// </summary>
    /// <returns>A blank DataTable with all columns defined (no rows).</returns>
    DataTable CreateDataTable();

    /// <summary>
    /// Generates TVP DataTable rows from search indices.
    /// </summary>
    /// <param name="resources">The resources to extract search parameters from.</param>
    /// <param name="resourceTypeIdMap">Mapping of resource type strings to their IDs.</param>
    /// <param name="searchParameterIdMap">Mapping of search parameter codes to their IDs.</param>
    /// <param name="resourceSurrogateIdMap">Mapping of resources to their allocated surrogate IDs (transactionId + index).</param>
    /// <returns>A populated DataTable ready for TVP parameter binding.</returns>
    DataTable GenerateRows(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap);
}
