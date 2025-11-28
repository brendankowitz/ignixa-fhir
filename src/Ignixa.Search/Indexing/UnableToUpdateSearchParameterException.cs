// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Search.Indexing;

/// <summary>
/// The exception that is thrown when if unable to update search parameter information from the data store
/// </summary>
public class UnableToUpdateSearchParameterException : FhirException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnableToUpdateSearchParameterException"/> class.
    /// </summary>
    public UnableToUpdateSearchParameterException()
        : base()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnableToUpdateSearchParameterException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public UnableToUpdateSearchParameterException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnableToUpdateSearchParameterException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public UnableToUpdateSearchParameterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnableToUpdateSearchParameterException"/> class.
    /// </summary>
    /// <param name="definitionUri">The search parameter definition URL.</param>
    public UnableToUpdateSearchParameterException(Uri definitionUri)
    {
        EnsureArg.IsNotNull(definitionUri, nameof(definitionUri));

        AddIssue(string.Format(Resources.UnableToUpdateSearchParameter, definitionUri.ToString()));
    }

    private void AddIssue(string diagnostics)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.NotSupported,
            Diagnostics = diagnostics
        });
    }
}
