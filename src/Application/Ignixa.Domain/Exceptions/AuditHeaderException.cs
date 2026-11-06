// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Domain.Exceptions;

/// <summary>
/// Base exception for custom audit header (<c>X-IGNIXA-AUDIT-*</c> / <c>X-MS-AZUREFHIR-AUDIT-*</c>) limit violations.
/// Results in HTTP 431 Request Header Fields Too Large, matching Azure Health Data Services.
/// Messages must never contain header values.
/// </summary>
public abstract class AuditHeaderException : FhirException
{
    protected AuditHeaderException(string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        Issues.Add(new OperationOutcomeIssue
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Invalid,
            Diagnostics = message
        });
    }

    /// <inheritdoc />
    public override int StatusCode => 431;
}
