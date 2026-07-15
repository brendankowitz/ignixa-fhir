// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Models;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Domain.Exceptions;

public class EverythingOperationException : FhirException
{
    public EverythingOperationException()
        : base()
    {
    }

    public EverythingOperationException(string message)
        : base(message)
    {
        Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");

        Issues.Add(new OperationOutcomeIssue()
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Invalid,
            Diagnostics = message
        });
    }

    public EverythingOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");

        Issues.Add(new OperationOutcomeIssue()
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Invalid,
            Diagnostics = message
        });
    }
}
