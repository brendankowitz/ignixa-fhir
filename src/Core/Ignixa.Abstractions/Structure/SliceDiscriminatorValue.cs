// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// The compiled expectation a slice places on one discriminator: for a candidate element to be
/// assigned to the slice, the value/type/presence at <see cref="Path"/> must satisfy this.
/// </summary>
/// <param name="Type">The discriminator type this expectation was derived for.</param>
/// <param name="Path">The discriminator path relative to the sliced element.</param>
/// <param name="ExpectedValue">
/// The expected match value derived from the slice's constraints:
/// for <see cref="DiscriminatorType.Value"/>/<see cref="DiscriminatorType.Pattern"/> the fixed/pattern
/// scalar (or, for an extension <c>url</c> discriminator, the slice's profile canonical);
/// for <see cref="DiscriminatorType.Type"/> the FHIR type code; <c>null</c> for
/// <see cref="DiscriminatorType.Exists"/> (presence only) or when the slice states no concrete
/// constraint at the path.
/// </param>
public sealed record SliceDiscriminatorValue(DiscriminatorType Type, string Path, string? ExpectedValue);
