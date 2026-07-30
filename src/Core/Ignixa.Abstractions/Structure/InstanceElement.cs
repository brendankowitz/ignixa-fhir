// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>A single evaluated element assignment for <see cref="InstanceCreationRequest"/>.</summary>
public sealed record InstanceElement(string Name, IReadOnlyList<IElement> Values);
