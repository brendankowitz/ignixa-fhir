// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Domain.Exceptions;

public class NotAcceptableException : FhirException
{
    public NotAcceptableException()
        : base()
    {
    }

    public NotAcceptableException(string message)
        : base(message)
    {
        Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty");

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.NotSupported,
            Diagnostics = message
        });
    }

    public NotAcceptableException(string message, Exception innerException)
        : base(message, innerException)
    {
        Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty");

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.NotSupported,
            Diagnostics = message
        });
    }

    /// <summary>
    /// Returns HTTP 406 Not Acceptable status code.
    /// </summary>
    public override int StatusCode => 406;
}
