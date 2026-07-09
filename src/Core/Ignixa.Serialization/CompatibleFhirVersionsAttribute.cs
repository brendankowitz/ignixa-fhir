// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Serialization;

/// <summary>
/// Declares which <see cref="FhirVersion"/>s a typed-model facade is valid for. Read by
/// <see cref="SourceNodes.ResourceJsonNode.As{T}"/> to guard against wrapping a node tagged with an
/// incompatible version in the wrong version's facade -- e.g. STU3 JSON reinterpreted through an
/// R4/R5-shaped accessor reads the right property names against the wrong shape, silently, since the
/// facades are untyped-JSON views with no structural validation of their own. Generated onto every
/// base and per-version facade (the base gets every version in its classification group; a per-version
/// subclass gets just its own version). Absent on hand-written, genuinely version-agnostic facades
/// (e.g. BundleJsonNode) -- the guard is a no-op for unmarked types, matching today's permissive
/// behavior for callers that don't track version at all.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CompatibleFhirVersionsAttribute : Attribute
{
    public CompatibleFhirVersionsAttribute(params FhirVersion[] versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (versions.Length == 0)
        {
            // An empty set would make As<T>() reject every version-tagged node forever (Array.IndexOf
            // on an empty array is always -1) while leaving untagged/Unspecified callers unaffected --
            // a type that's silently unusable for exactly the callers who bothered to track version.
            // No generator call site produces this today, but nothing else stops one from being added
            // later; catching it here turns that into a build-time codegen failure instead.
            throw new ArgumentException("At least one FhirVersion must be specified.", nameof(versions));
        }

        Versions = versions;
    }

    public IReadOnlyList<FhirVersion> Versions { get; }
}
