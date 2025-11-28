// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;

namespace Ignixa.Serialization.Abstractions;

public abstract class FhirException : Exception
{
    protected FhirException()
        : base()
    {
    }

    protected FhirException(string? message)
        : base(message)
    {
    }

    protected FhirException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    protected FhirException(params OperationOutcomeJsonNode.IssueComponent[] issues)
        : this(null!, issues)
    {
    }

    protected FhirException(string? message, params OperationOutcomeJsonNode.IssueComponent[]? issues)
        : this(message, null!, issues)
    {
    }

    protected FhirException(string? message, Exception? innerException, params OperationOutcomeJsonNode.IssueComponent[]? issues)
        : base(message, innerException)
    {
        if (issues != null)
            foreach (OperationOutcomeJsonNode.IssueComponent issue in issues)
                Issues.Add(issue);
    }

    public ICollection<OperationOutcomeJsonNode.IssueComponent> Issues { get; } = new List<OperationOutcomeJsonNode.IssueComponent>();

    /// <summary>
    /// Gets the HTTP status code for this exception. Default is 400 (Bad Request).
    /// </summary>
    public virtual int StatusCode => 400;

    /// <summary>
    /// Gets the OperationOutcome for this exception.
    /// </summary>
    public virtual OperationOutcomeJsonNode OperationOutcome
    {
        get
        {
            var outcome = new OperationOutcomeJsonNode();
            foreach (var issue in Issues)
            {
                outcome.Issue.Add(issue);
            }
            return outcome;
        }
    }
}
