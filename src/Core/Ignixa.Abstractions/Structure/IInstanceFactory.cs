// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Constructs a FHIR object for a FHIRPath instance selector expression
/// (<c>Type { element: value, ... }</c>). Implemented by the host so the FHIRPath
/// engine stays model-agnostic; the returned node should be the same kind the
/// engine navigates elsewhere (e.g. a schema-aware, source-node-backed element).
/// </summary>
public interface IInstanceFactory
{
    /// <summary>
    /// Creates an instance of <paramref name="typeName"/> populated with the
    /// supplied element values. Elements whose value expression evaluated to an
    /// empty collection are already omitted per the FHIRPath spec.
    /// </summary>
    /// <param name="typeName">Unqualified type name (e.g. "Coding").</param>
    /// <param name="namespacePrefix">Optional namespace (e.g. "FHIR" from "FHIR.Coding"), or null.</param>
    /// <param name="elements">Evaluated element assignments, in source order.</param>
    /// <returns>The created element, or null if the host cannot construct the type.</returns>
    IElement? Create(string typeName, string? namespacePrefix, IReadOnlyList<InstanceElement> elements);
}

/// <summary>A single evaluated element assignment for <see cref="IInstanceFactory"/>.</summary>
public sealed record InstanceElement(string Name, IReadOnlyList<IElement> Values);
