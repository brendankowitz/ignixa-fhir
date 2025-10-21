// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SourceNodeSerialization.Models;

namespace Ignixa.Domain.Exceptions;

public abstract class FhirException : Exception
{
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
            var outcome = new OperationOutcomeJsonNode
            {
                Issue = Issues.ToList()
            };
            return outcome;
        }
    }
}
