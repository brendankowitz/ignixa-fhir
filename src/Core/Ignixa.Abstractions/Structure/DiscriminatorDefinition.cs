// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// A single slice discriminator from <c>ElementDefinition.slicing.discriminator</c>: the
/// <see cref="Type"/> (how to test) plus the restricted-FHIRPath <see cref="Path"/> (where to look).
/// </summary>
/// <param name="Type">The discriminator type (value / pattern / exists / type / profile).</param>
/// <param name="Path">
/// The discriminator path relative to the sliced element (e.g. <c>url</c>, <c>system</c>,
/// <c>$this</c>). Restricted FHIRPath: simple navigation plus <c>resolve()</c>,
/// <c>extension(url)</c>, and <c>ofType()</c>.
/// </param>
public sealed record DiscriminatorDefinition(DiscriminatorType Type, string Path);
