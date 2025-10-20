// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.ConditionalOperations;

/// <summary>
/// Exception thrown when a conditional operation fails validation or encounters an error.
/// </summary>
public class ConditionalOperationException : Exception
{
    /// <summary>
    /// Gets the conditional operation type (e.g., "ConditionalCreate", "ConditionalUpdate").
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Gets the number of resources that matched the search criteria.
    /// </summary>
    public int MatchCount { get; }

    /// <summary>
    /// Gets the search criteria that was used for the conditional operation.
    /// </summary>
    public string? SearchCriteria { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionalOperationException"/> class.
    /// </summary>
    /// <param name="operation">The conditional operation type.</param>
    /// <param name="message">The error message.</param>
    /// <param name="matchCount">The number of resources that matched the search criteria.</param>
    /// <param name="searchCriteria">The search criteria that was used.</param>
    public ConditionalOperationException(
        string operation,
        string message,
        int matchCount = 0,
        string? searchCriteria = null)
        : base(message)
    {
        Operation = operation;
        MatchCount = matchCount;
        SearchCriteria = searchCriteria;
    }
}
