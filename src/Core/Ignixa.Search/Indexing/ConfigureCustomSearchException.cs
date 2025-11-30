// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Search.Indexing;

/// <summary>
/// The exception that is thrown when the search parameter is not supported.
/// </summary>
public class ConfigureCustomSearchException : FhirException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureCustomSearchException"/> class.
    /// </summary>
    public ConfigureCustomSearchException()
        : base()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureCustomSearchException"/> class.
    /// </summary>
    /// <param name="error">The error message to include in the operation outcome issues list.</param>
    public ConfigureCustomSearchException(string error)
    {
        Debug.Assert(!string.IsNullOrEmpty(error), "Exception message should not be empty");

        AddIssue(error);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureCustomSearchException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConfigureCustomSearchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private void AddIssue(string diagnostics)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Exception,
            Diagnostics = diagnostics
        });
    }
}
