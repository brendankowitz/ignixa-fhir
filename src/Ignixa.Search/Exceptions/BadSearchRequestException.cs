// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Search.Exceptions;

public class BadSearchRequestException : FhirException
{
    public BadSearchRequestException()
        : base()
    {
    }

    public BadSearchRequestException(string message)
        : base(message)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Invalid,
            Diagnostics = message
        });
    }

    public BadSearchRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Invalid,
            Diagnostics = message
        });
    }
}
