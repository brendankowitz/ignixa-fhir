// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;
using Ignixa.Extensions.Models;

namespace Ignixa.Extensions.Exceptions;

/// <summary>
/// The exception that is thrown when the resource is not supported.
/// </summary>
public class ResourceNotSupportedException : FhirException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceNotSupportedException"/> class.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    public ResourceNotSupportedException(Type resourceType)
        : this(resourceType?.Name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceNotSupportedException"/> class.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    public ResourceNotSupportedException(string resourceType)
    {
        EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

        Issues.Add(new OperationOutcomeIssue(
            OperationOutcomeConstants.IssueSeverity.Error,
            OperationOutcomeConstants.IssueType.NotSupported,
            string.Format(CultureInfo.CurrentCulture, $"{resourceType} not supported", resourceType)));
    }
}
