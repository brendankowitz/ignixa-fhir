// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Npm;

/// <summary>What the registry's package document says about one package.</summary>
/// <param name="Id">The package id.</param>
/// <param name="Latest">The <c>dist-tags.latest</c> version; null when the document has none.</param>
/// <param name="Shasums">The published SHA-1 (<c>dist.shasum</c>, hex) of each version that has one.</param>
public sealed record RegistryPackage(string Id, string? Latest, IReadOnlyDictionary<string, string> Shasums);
