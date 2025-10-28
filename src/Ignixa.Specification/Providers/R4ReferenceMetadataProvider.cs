// <copyright file="R4ReferenceMetadataProvider.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;
using Ignixa.Specification.Generated;

namespace Ignixa.Specification.Providers;

/// <summary>
/// Provides R4-specific reference metadata by wrapping the generated R4ReferenceMetadata class.
/// </summary>
[CLSCompliant(false)]
public sealed class R4ReferenceMetadataProvider : IReferenceMetadataProvider
{
    /// <summary>
    /// Gets a singleton instance of the R4ReferenceMetadataProvider.
    /// </summary>
    public static R4ReferenceMetadataProvider Instance { get; } = new R4ReferenceMetadataProvider();

    private R4ReferenceMetadataProvider()
    {
    }

    /// <inheritdoc/>
    public IReadOnlyList<ReferenceFieldMetadata> GetMetadata(string resourceType)
    {
        return R4ReferenceMetadata.GetMetadata(resourceType);
    }

    /// <inheritdoc/>
    public bool HasReferences(string resourceType)
    {
        return R4ReferenceMetadata.HasReferences(resourceType);
    }
}
