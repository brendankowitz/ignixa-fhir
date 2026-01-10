// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

// Polyfill for IsExternalInit which is required for init-only properties and records
// when targeting .NET Standard 2.0 (source generators must target netstandard2.0)

namespace System.Runtime.CompilerServices;

internal sealed class IsExternalInit { }
