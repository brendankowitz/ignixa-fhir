// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Sparky.Extensions.Models;

namespace Sparky.Extensions.Exceptions;

public class RequestNotValidException : FhirException
{
    public RequestNotValidException(string message)
        : base(message)
    {
        EnsureArg.IsNotNull(message, nameof(message));

        Issues.Add(new OperationOutcomeIssue(
            OperationOutcomeConstants.IssueSeverity.Error,
            OperationOutcomeConstants.IssueType.Invalid,
            message));
    }
}
