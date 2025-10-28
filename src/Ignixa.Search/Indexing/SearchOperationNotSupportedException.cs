// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Constants;
using Ignixa.Serialization.Models;

namespace Ignixa.Search.Indexing;

/// <summary>
/// Thrown when search operation is not supported.
/// </summary>
public class SearchOperationNotSupportedException : FhirException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchOperationNotSupportedException"/> class.
    /// </summary>
    public SearchOperationNotSupportedException()
        : base()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchOperationNotSupportedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public SearchOperationNotSupportedException(string message)
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(message), $"{nameof(message)} should not be null or whitespace.");

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.NotSupported,
            Diagnostics = message
        });
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchOperationNotSupportedException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public SearchOperationNotSupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
