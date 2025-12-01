// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Domain.Exceptions;

/// <summary>
/// Exception thrown when a requested operation is not yet implemented.
/// Returns HTTP 501 Not Implemented.
/// </summary>
public class NotImplementedException : FhirException
{
    public NotImplementedException()
        : base()
    {
    }

    public NotImplementedException(string message)
        : base(message)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.NotSupported,
            Diagnostics = message
        });
    }

    public NotImplementedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.NotSupported,
            Diagnostics = message
        });
    }

    public override int StatusCode => 501;
}
