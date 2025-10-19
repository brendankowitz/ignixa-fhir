// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SourceNodeSerialization.SourceNodes.Models;

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
}
