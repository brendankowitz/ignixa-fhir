// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Search.Exceptions;

/// <summary>
/// Exception thrown when a search operation is understood but forbidden.
/// Results in HTTP 403 Forbidden response.
/// </summary>
public class ForbiddenSearchException : FhirException
{
    public ForbiddenSearchException()
        : base()
    {
    }

    public ForbiddenSearchException(string message)
        : base(message)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Forbidden,
            Diagnostics = message
        });
    }

    public ForbiddenSearchException(string message, Exception innerException)
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
