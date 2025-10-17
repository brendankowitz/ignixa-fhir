// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Extensions.Models;

namespace Ignixa.Extensions.Exceptions;

public class MethodNotAllowedException : FhirException
{
    public MethodNotAllowedException(string message)
        : base(message)
    {
        Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty");

        Issues.Add(new OperationOutcomeIssue(
            OperationOutcomeConstants.IssueSeverity.Error,
            OperationOutcomeConstants.IssueType.Forbidden,
            message));
    }
}
