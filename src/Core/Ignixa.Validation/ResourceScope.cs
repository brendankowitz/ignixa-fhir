// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Validation;

/// <summary>
/// FHIRPath tree-context scope threaded through validation. Carries the resource-rooted variables
/// (<c>%resource</c>, <c>%rootResource</c>) so a constraint evaluated deep inside a resource sees the
/// correct enclosing resource — without the runtime element model exposing a parent pointer.
/// </summary>
/// <remarks>
/// <para>
/// Immutable. Forked at resource boundaries via <see cref="ValidationState.EnterRootResource"/>
/// and <see cref="ValidationState.EnterContainedResource"/>. Never re-pointed per element:
/// <c>%resource</c> must remain the enclosing resource, not the constrained sub-element.
/// </para>
/// <para>
/// This scope deliberately carries NO resolver. Reference resolution is
/// <c>Ignixa.FhirPath.Evaluation.ReferenceIndex</c>'s job, and there is exactly one implementation of
/// it; each consumer builds that index from this scope's <c>RootResource ?? Resource</c> for itself:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Anything reached through FHIRPath — <c>resolve()</c> inside an invariant
/// (<c>FhirPathInvariantCheck</c>) or a slicing discriminator (<c>SlicingCheck</c>) — is served by
/// <c>EvaluationContext.ReferenceIndexCache</c>, which builds the index lazily from the
/// <c>Resource</c>/<c>RootResource</c> those checks copy onto the evaluation context.
/// </description></item>
/// <item><description>
/// <c>ReferenceResolutionCheck</c>, the one consumer that is not a FHIRPath evaluation, builds its
/// own index directly.
/// </description></item>
/// </list>
/// <para>
/// An earlier design also hung a memoised in-instance resolver off this scope and passed it as
/// <c>FhirEvaluationContext.ElementResolver</c>. That made it a second, weaker implementation of the
/// same rule layered behind the first: it could not resolve a bare <c>#</c> and it ignored the focus
/// location, so it never answered a reference the index had not already answered — it only rebuilt an
/// identical index on every miss (measured: 2 builds per unresolved reference at root scope, 3 at
/// contained scope). Do not reintroduce it. <c>ElementResolver</c> remains the seam for a HOST-supplied
/// resolver that reaches outside the instance (a repository lookup, say); validation has no such host
/// today, which is why neither check sets it.
/// </para>
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
}
