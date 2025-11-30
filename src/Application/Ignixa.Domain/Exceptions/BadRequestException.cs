// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Domain.Exceptions;

public class BadRequestException : FhirException
{
    public BadRequestException()
        : base()
    {
    }

    public BadRequestException(string message)
        : base(message)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Invalid,
            Diagnostics = message
        });
    }

    public BadRequestException(string message, Exception innerException)
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
