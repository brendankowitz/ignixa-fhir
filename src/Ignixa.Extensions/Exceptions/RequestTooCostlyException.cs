// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Extensions.Models;

namespace Ignixa.Extensions.Exceptions;

public class RequestTooCostlyException : FhirException
{
    public RequestTooCostlyException(string message)
        : base(message)
    {
        EnsureArg.IsNotNull(message, nameof(message));

        Issues.Add(new OperationOutcomeIssue(
            OperationOutcomeConstants.IssueSeverity.Error,
            OperationOutcomeConstants.IssueType.TooCostly,
            message));
    }
}
