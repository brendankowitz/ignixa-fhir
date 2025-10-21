// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SourceNodeSerialization.Models;

namespace Ignixa.Domain.Exceptions;

public class BadRequestException : FhirException
{
    public BadRequestException(string errorMessage)
        : base(errorMessage)
    {
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Invalid,
            Diagnostics = errorMessage
        });
    }
}
