// <copyright file="R6CoreSchemaProvider.Partial.cs" company="Ignixa Contributors">
//     Copyright (c) Ignixa Contributors. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System;
using Ignixa.Abstractions;

namespace Ignixa.Specification.Generated;

public sealed partial class R6CoreSchemaProvider
{
    private readonly Lazy<IReferenceMetadataProvider> _referenceMetadataProvider
        = new(() => new R6ReferenceMetadata());

    public IReferenceMetadataProvider ReferenceMetadataProvider => _referenceMetadataProvider.Value;

    private readonly Lazy<IValueSetProvider> _valueSetProvider
        = new(() => new R6ValueSetProvider());

    public IValueSetProvider ValueSetProvider => _valueSetProvider.Value;
}
