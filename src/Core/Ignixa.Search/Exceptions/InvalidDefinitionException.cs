// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Search.Exceptions;

/// <summary>
/// The exception that is thrown when provided definition is invalid.
/// </summary>
public class InvalidDefinitionException : FhirException
{
    public InvalidDefinitionException()
        : base()
    {
    }

    public InvalidDefinitionException(string message)
        : base(message)
    {
    }

    public InvalidDefinitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InvalidDefinitionException(string message, OperationOutcomeJsonNode.IssueComponent[] issues = null)
        : base(message, issues)
    {
    }
}
