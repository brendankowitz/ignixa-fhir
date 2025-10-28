// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SourceNodeSerialization.Models;

namespace Ignixa.Domain.Exceptions;

public class UnsupportedConfigurationException : FhirException
{
    public UnsupportedConfigurationException()
        : base()
    {
    }

    public UnsupportedConfigurationException(string message)
        : base(message)
    {
    }

    public UnsupportedConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public UnsupportedConfigurationException(string message, OperationOutcomeJsonNode.IssueComponent[]? issues = null)
        : base(message, issues)
    {
    }
}
