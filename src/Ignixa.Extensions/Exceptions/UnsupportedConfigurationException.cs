// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Extensions.Models;

namespace Ignixa.Extensions.Exceptions;

public class UnsupportedConfigurationException : FhirException
{
    public UnsupportedConfigurationException(string message, OperationOutcomeIssue[] issues = null)
        : base(message, issues)
    {
    }
}
