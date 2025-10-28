// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Serialization.Models;

namespace Ignixa.Domain.Exceptions;

public class RequestNotValidException : FhirException
{
    public RequestNotValidException()
        : base()
    {
    }

    public RequestNotValidException(string message)
        : base(message)
    {
        EnsureArg.IsNotNull(message, nameof(message));

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Invalid,
            Diagnostics = message
        });
    }

    public RequestNotValidException(string message, Exception innerException)
        : base(message, innerException)
    {
        EnsureArg.IsNotNull(message, nameof(message));

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Invalid,
            Diagnostics = message
        });
    }
}
