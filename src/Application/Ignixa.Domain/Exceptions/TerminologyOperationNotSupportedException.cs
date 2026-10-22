// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Models;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Domain.Exceptions;

/// <summary>
/// Exception thrown when terminology content does not support a requested operation.
/// </summary>
public class TerminologyOperationNotSupportedException : FhirException
{
    public TerminologyOperationNotSupportedException(string message)
        : base(message)
    {
        EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));

        Issues.Add(new OperationOutcomeIssue()
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.NotSupported,
            Diagnostics = message
        });
    }

    public override int StatusCode => 422;
}
