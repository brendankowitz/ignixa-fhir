// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents the union operator for combining query results.
/// </summary>
public enum UnionOperator
{
    /// <summary>
    /// UNION ALL - includes duplicates
    /// </summary>
    All = 0,

    /// <summary>
    /// UNION DISTINCT - removes duplicates
    /// </summary>
    Distinct = 1
}
