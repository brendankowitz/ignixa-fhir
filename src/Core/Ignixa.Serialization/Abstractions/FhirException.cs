// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Models;

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

    protected FhirException(params OperationOutcomeIssue[] issues)
        : this(null!, issues)
    {
    }

    protected FhirException(string? message, params OperationOutcomeIssue[]? issues)
        : this(message, null!, issues)
    {
    }

    protected FhirException(string? message, Exception? innerException, params OperationOutcomeIssue[]? issues)
        : base(message, innerException)
    {
        if (issues != null)
            foreach (OperationOutcomeIssue issue in issues)
                Issues.Add(issue);
    }

    public ICollection<OperationOutcomeIssue> Issues { get; } = new List<OperationOutcomeIssue>();

    /// <summary>
    /// Gets the HTTP status code for this exception. Default is 400 (Bad Request).
    /// </summary>
    public virtual int StatusCode => 400;

    /// <summary>
    /// Gets the OperationOutcome for this exception.
    /// </summary>
    public virtual OperationOutcome OperationOutcome
    {
        get
        {
            var outcome = new OperationOutcome();
            foreach (var issue in Issues)
            {
                outcome.Issue.Add(issue);
            }
            return outcome;
        }
    }
}
