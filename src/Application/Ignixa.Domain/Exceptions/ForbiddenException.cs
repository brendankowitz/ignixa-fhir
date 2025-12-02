// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Domain.Exceptions;

/// <summary>
/// Exception thrown when an operation is understood but forbidden.
/// Results in HTTP 403 Forbidden response.
/// </summary>
public class ForbiddenException : FhirException
{
    public ForbiddenException()
        : base()
    {
    }

    public ForbiddenException(string message)
        : base(message)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Forbidden,
            Diagnostics = message
        });
    }

    public ForbiddenException(string message, Exception innerException)
        : base(message, innerException)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Forbidden,
            Diagnostics = message
        });
    }

    /// <inheritdoc />
    public override int StatusCode => 403;
}
