// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.SourceNodeSerialization.Models;

namespace Ignixa.Domain.Exceptions;

public class EverythingOperationException : FhirException
{
    public EverythingOperationException(string message)
        : base(message)
    {
        Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Invalid,
            Diagnostics = message
        });
    }
}
