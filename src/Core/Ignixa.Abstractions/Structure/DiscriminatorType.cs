// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// The FHIR slice discriminator type (<c>ElementDefinition.slicing.discriminator.type</c>).
/// Determines how a candidate element is tested against a slice's constraint on the
/// discriminator <c>path</c>.
/// </summary>
public enum DiscriminatorType
{
    /// <summary>Match on a fixed/pattern scalar value at the discriminator path (e.g. Extension.url).</summary>
    Value,

    /// <summary>Match on a pattern element at the discriminator path.</summary>
    Pattern,

    /// <summary>Match on presence/absence of the discriminator path.</summary>
    Exists,

    /// <summary>Match on the FHIR type of the element at the discriminator path.</summary>
    Type,

    /// <summary>Match on conformance to a profile canonical. Requires <c>conformsTo()</c>; deferred.</summary>
    Profile,
}
