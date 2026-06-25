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
/// Immutable. Forked at resource boundaries via <see cref="ValidationState.EnterRootResource"/>
/// and <see cref="ValidationState.EnterContainedResource"/>. Never re-pointed per element:
/// <c>%resource</c> must remain the enclosing resource, not the constrained sub-element.
/// </remarks>
public record ResourceScope
{
    /// <summary>
    /// Gets the nearest containing resource (the FHIRPath <c>%resource</c> variable).
    /// </summary>
    public IElement? Resource { get; init; }

    /// <summary>
    /// Gets the container/parent resource (the FHIRPath <c>%rootResource</c> variable).
    /// Equals <see cref="Resource"/> for a standalone resource or an independent Bundle entry;
    /// points at the containing resource for a contained resource.
    /// </summary>
    public IElement? RootResource { get; init; }

    /// <summary>
    /// Gets the reference resolver backing the FHIRPath <c>resolve()</c> function. Returns the
    /// target <see cref="IElement"/> for a reference, or null when it does not resolve.
    /// </summary>
    public Func<string, IElement?>? Resolver { get; init; }
}
