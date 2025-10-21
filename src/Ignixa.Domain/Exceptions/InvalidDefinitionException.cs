// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SourceNodeSerialization.Models;

namespace Ignixa.Domain.Exceptions;

/// <summary>
/// The exception that is thrown when provided definition is invalid.
/// </summary>
public class InvalidDefinitionException : FhirException
{
    public InvalidDefinitionException(string message, OperationOutcomeJsonNode.IssueComponent[]? issues = null)
        : base(message, issues)
    {
    }
}
