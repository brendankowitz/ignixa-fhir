// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Sparky.Extensions.Models;

namespace Sparky.Extensions.Exceptions;

public class BadRequestException : FhirException
{
    public BadRequestException(string errorMessage)
        : base(errorMessage)
    {
        Issues.Add(new OperationOutcomeIssue(
            OperationOutcomeConstants.IssueSeverity.Error,
            OperationOutcomeConstants.IssueType.Invalid,
            errorMessage));
    }
}
