// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions;

/// <summary>One context an extension definition allows (<c>StructureDefinition.context</c>).</summary>
/// <param name="Type">How <paramref name="Expression"/> is read.</param>
/// <param name="Expression">An element path or type name, an extension url, or a FHIRPath expression.</param>
public sealed record ExtensionContext(ExtensionContextType Type, string Expression);
