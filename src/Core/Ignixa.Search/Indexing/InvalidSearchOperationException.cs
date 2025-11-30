// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Search.Indexing;

/// <summary>
/// Exception thrown when an invalid search operation is specified.
/// </summary>
public class InvalidSearchOperationException : FhirException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidSearchOperationException"/> class.
    /// </summary>
    public InvalidSearchOperationException()
        : base()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidSearchOperationException"/> class.
    /// </summary>
    /// <param name="message">The message to display.</param>
    public InvalidSearchOperationException(string message)
        : base(message)
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(message), $"{nameof(message)} should not be null.");

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Invalid,
            Diagnostics = message
        });
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidSearchOperationException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public InvalidSearchOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
