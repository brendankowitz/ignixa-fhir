// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Tells the resource type a reference names when the resource being validated cannot: a <c>urn:uuid:</c> that a Bundle the
/// resource was extracted from resolves, say, or a server's own logical references. Consulted by reference target-type
/// validation before it reads the type from the reference's own shape (<c>Type/id</c>, an absolute RESTful URL, a contained
/// <c>#id</c>).
/// </summary>
/// <remarks>
/// Set on <see cref="ValidationSettings.ReferenceTypeResolver"/>. Its answers may depend on which resource is being validated
/// (a Bundle's urns name its own entries only); such a resolver belongs on a settings instance used for that resource alone.
/// Called on the validating thread; an implementation shared across concurrent validations must be thread-safe.
/// </remarks>
public interface IReferenceTypeResolver
{
    /// <summary>
    /// The resource type <paramref name="reference"/> names, or null when the host cannot tell it.
    /// </summary>
    /// <param name="reference">The <c>Reference.reference</c> value, as written.</param>
    /// <returns>A resource type name (e.g. <c>Patient</c>), or null.</returns>
    string? ResolveResourceType(string reference);
}
