// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// The validator's answer for the references of one resource that the folder's Bundles resolve (a <c>urn:uuid:</c> naming another
/// entry): the type of the <c>Type/id</c> each resolves to. For that resource only.
/// </summary>
/// <param name="resolved">The resource's Bundle references as written and the <c>Type/id</c> each resolves to.</param>
internal sealed class BundleReferenceTypes(IReadOnlyDictionary<string, string> resolved) : IReferenceTypeResolver
{
    public string? ResolveResourceType(string reference) =>
        resolved.GetValueOrDefault(reference) is { } target && target.IndexOf('/', StringComparison.Ordinal) is > 0 and var slash ? target[..slash] : null;
}
