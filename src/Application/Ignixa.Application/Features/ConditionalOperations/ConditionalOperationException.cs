// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Exceptions;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Application.Features.ConditionalOperations;

/// <summary>
/// Exception thrown when a conditional operation fails validation or encounters an error.
/// </summary>
public class ConditionalOperationException : FhirException
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
    /// Gets the HTTP status code for this exception.
    /// 0 matches: 404 Not Found
    /// Multiple matches: 412 Precondition Failed
    /// </summary>
    public override int StatusCode { get; }

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
        : base(message, CreateIssue(message, matchCount, searchCriteria))
    {
        Operation = operation;
        MatchCount = matchCount;
        SearchCriteria = searchCriteria;

        // 0 matches: 404 Not Found
        // Multiple matches: 412 Precondition Failed
        StatusCode = matchCount == 0 ? 404 : 412;
    }

    private static OperationOutcomeJsonNode.IssueComponent CreateIssue(string message, int matchCount, string? searchCriteria)
    {
        var issueCode = matchCount == 0 ? OperationOutcomeJsonNode.IssueType.NotFound : OperationOutcomeJsonNode.IssueType.Duplicate;

        var issue = new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = issueCode,
            Diagnostics = message
        };

        if (!string.IsNullOrEmpty(searchCriteria))
        {
            issue.Expression.Add(searchCriteria);
        }

        return issue;
    }
}
