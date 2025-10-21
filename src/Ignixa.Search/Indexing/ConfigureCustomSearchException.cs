// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Constants;
using Ignixa.SourceNodeSerialization.Models;

namespace Ignixa.Search.Indexing;

/// <summary>
/// The exception that is thrown when the search parameter is not supported.
/// </summary>
public class ConfigureCustomSearchException : FhirException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureCustomSearchException"/> class.
    /// </summary>
    /// <param name="error">The error message to include in the operation outcome issues list.</param>
    public ConfigureCustomSearchException(string error)
    {
        Debug.Assert(!string.IsNullOrEmpty(error), "Exception message should not be empty");

        AddIssue(error);
    }

    private void AddIssue(string diagnostics)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Exception,
            Diagnostics = diagnostics
        });
    }
}
