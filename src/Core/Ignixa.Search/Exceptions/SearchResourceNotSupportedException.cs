// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Search.Exceptions;

/// <summary>
/// The exception that is thrown when the resource is not supported.
/// </summary>
public class SearchResourceNotSupportedException : FhirException
{
    public SearchResourceNotSupportedException()
        : base()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchResourceNotSupportedException"/> class.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    public SearchResourceNotSupportedException(string resourceType)
        : base(string.Format(CultureInfo.CurrentCulture, $"{resourceType} not supported", resourceType))
    {
        EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.NotSupported,
            Diagnostics = string.Format(CultureInfo.CurrentCulture, $"{resourceType} not supported", resourceType)
        });
    }

    public SearchResourceNotSupportedException(string resourceType, Exception innerException)
        : base(string.Format(CultureInfo.CurrentCulture, $"{resourceType} not supported", resourceType), innerException)
    {
        EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

        Issues.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.NotSupported,
            Diagnostics = string.Format(CultureInfo.CurrentCulture, $"{resourceType} not supported", resourceType)
        });
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchResourceNotSupportedException"/> class.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    public SearchResourceNotSupportedException(Type resourceType)
        : this(resourceType?.Name ?? "Unknown")
    {
    }
}
