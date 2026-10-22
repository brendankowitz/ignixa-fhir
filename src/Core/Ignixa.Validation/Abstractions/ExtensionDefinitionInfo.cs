// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions;

/// <summary>What an extension definition says about its use, beyond its elements.</summary>
/// <param name="Contexts">Where the extension may be used (<c>StructureDefinition.context</c>); empty when it states none.</param>
/// <param name="IsModifier">Whether it is a modifier extension (the root element's <c>isModifier</c>).</param>
public sealed record ExtensionDefinitionInfo(IReadOnlyList<ExtensionContext> Contexts, bool IsModifier);
