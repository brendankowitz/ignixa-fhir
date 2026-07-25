// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Models;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Domain.Exceptions;

/// <summary>
/// Exception thrown when an operation fails due to a server-side/infrastructure fault, not a problem
/// with the request itself.
/// Results in HTTP 500 Internal Server Error response.
/// </summary>
public class InternalServerErrorException : FhirException
{
    public InternalServerErrorException()
        : base()
    {
    }

    public InternalServerErrorException(string message)
        : base(message)
    {
        Issues.Add(new OperationOutcomeIssue()
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Exception,
            Diagnostics = message
        });
    }

    public InternalServerErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
        Issues.Add(new OperationOutcomeIssue()
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Exception,
            Diagnostics = message
        });
    }

    /// <inheritdoc />
    public override int StatusCode => 500;
}
