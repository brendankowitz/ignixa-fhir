// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;

namespace Ignixa.Domain.Exceptions;

public class MethodNotAllowedException : FhirException
{
    public MethodNotAllowedException()
        : base()
    {
    }

    public MethodNotAllowedException(string message)
        : base(message)
    {
        Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty");

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Forbidden,
            Diagnostics = message
        });
    }

    public MethodNotAllowedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty");

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Forbidden,
            Diagnostics = message
        });
    }
}
