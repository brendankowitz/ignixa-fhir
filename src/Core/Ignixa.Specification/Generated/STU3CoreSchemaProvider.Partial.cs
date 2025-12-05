// <copyright file="STU3CoreSchemaProvider.Partial.cs" company="Ignixa Contributors">
//     Copyright (c) Ignixa Contributors. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System;
using Ignixa.Abstractions;

namespace Ignixa.Specification.Generated;

public sealed partial class STU3CoreSchemaProvider
{
    private readonly Lazy<IReferenceMetadataProvider> _referenceMetadataProvider
        = new(() => new Stu3ReferenceMetadata());

    public IReferenceMetadataProvider ReferenceMetadataProvider => _referenceMetadataProvider.Value;
}
