// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Thrown by an <see cref="ITerminologyService"/> for an operation it cannot perform (the in-memory service's
/// <c>$subsumes</c>): the server's limitation, not a fault, so it is answered as HTTP 501 with a <c>not-supported</c> issue.
/// A dedicated type, so that a <see cref="NotSupportedException"/> from anywhere else (a stream that cannot seek) stays a 500.
/// </summary>
public sealed class TerminologyOperationNotSupportedException : FhirException
{
    public TerminologyOperationNotSupportedException()
    {
    }

    public TerminologyOperationNotSupportedException(string message)
        : this(message, null)
    {
    }

    public TerminologyOperationNotSupportedException(string message, Exception? innerException)
        : base(message, innerException)
    {
        Issues.Add(new OperationOutcomeIssue
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.NotSupported,
            Diagnostics = message,
        });
    }

    public override int StatusCode => 501;
}
