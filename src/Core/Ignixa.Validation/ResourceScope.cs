// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Validation;

/// <summary>
/// FHIRPath tree-context scope threaded through validation. Carries the resource-rooted
/// variables (<c>%resource</c>, <c>%rootResource</c>) and the <c>resolve()</c> delegate so a
/// constraint evaluated deep inside a resource sees the correct enclosing resource and can
/// resolve sibling references — without the runtime element model exposing a parent pointer.
/// </summary>
/// <remarks>
/// Immutable, and constructed only through the factories so the three fields move together: a
/// scope is either fully seeded (all three set, <see cref="IsSeeded"/> true) or the
/// <see cref="Unseeded"/> sentinel (all null). This makes a partially-seeded scope
/// unrepresentable, so every consumer agrees on whether tree-context is active.
/// Forked at resource boundaries via <see cref="ValidationState.EnterRootResource"/> and
/// <see cref="ValidationState.EnterContainedResource"/>. Never re-pointed per element:
/// <c>%resource</c> must remain the enclosing resource, not the constrained sub-element.
/// </remarks>
public sealed record ResourceScope
{
    /// <summary>
    /// The unseeded scope: no resource context. Direct callers/tests that validate without seeding
    /// run context-free (<c>%resource</c>/<c>%rootResource</c> unbound, <c>resolve()</c> unavailable).
    /// </summary>
    public static readonly ResourceScope Unseeded = new(null, null, null);

    private ResourceScope(IElement? resource, IElement? rootResource, Func<string, IElement?>? resolver)
    {
        Resource = resource;
        RootResource = rootResource;
        Resolver = resolver;
    }

    /// <summary>
    /// Gets the nearest containing resource (the FHIRPath <c>%resource</c> variable).
    /// </summary>
    public IElement? Resource { get; }

    /// <summary>
    /// Gets the container/parent resource (the FHIRPath <c>%rootResource</c> variable).
    /// Equals <see cref="Resource"/> for a standalone resource; points at the containing resource
    /// for a contained resource.
    /// </summary>
    public IElement? RootResource { get; }

    /// <summary>
    /// Gets the reference resolver backing the FHIRPath <c>resolve()</c> function. Returns the
    /// target <see cref="IElement"/> for a reference, or null when it does not resolve.
    /// </summary>
    public Func<string, IElement?>? Resolver { get; }

    /// <summary>
    /// Gets a value indicating whether resource context has been seeded. The single source of truth
    /// for "is tree-context active?" — keying off any one field is equivalent because the factories
    /// seed all three atomically.
    /// </summary>
    public bool IsSeeded => Resource is not null;

    /// <summary>
    /// Creates a scope for a resource that is its own validation root: <c>%resource</c> and
    /// <c>%rootResource</c> both point at the resource itself.
    /// </summary>
    internal static ResourceScope Root(IElement resource, Func<string, IElement?> resolver)
        => new(resource, resource, resolver);

    /// <summary>
    /// Creates a scope for a contained resource C inside parent resource P: <c>%resource</c> is C
    /// and <c>%rootResource</c> is P (or null when C is entered without a seeded root).
    /// </summary>
    internal static ResourceScope Contained(IElement contained, IElement? rootResource, Func<string, IElement?> resolver)
        => new(contained, rootResource, resolver);
}
