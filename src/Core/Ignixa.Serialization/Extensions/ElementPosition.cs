// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Serialization.Extensions;

/// <summary>
/// Where a JSON property sorts within its parent object, and the type definition of its value.
/// </summary>
/// <param name="Rank">Sort key; lower ranks are written first.</param>
/// <param name="Type">Definition of the property's value, or null when the schema does not describe it.</param>
internal readonly record struct ElementPosition(int Rank, IType? Type);
