// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.CodeAnalysis.Testing;

namespace Ignixa.Analyzers.Tests;

/// <summary>
/// .NET 10 reference assemblies for the Roslyn analyzer test harness.
/// </summary>
/// <remarks>
/// Microsoft.CodeAnalysis.*.Testing 1.1.2 predates .NET 10, so <c>ReferenceAssemblies.Net</c> stops at
/// Net90. Compiling the harness against Net90 while referencing Ignixa.Serialization -- which targets
/// net10.0 -- fails with CS1705 before the analyzer under test is ever reached.
/// </remarks>
internal static class ReferenceAssembliesProvider
{
    public static readonly ReferenceAssemblies Net100 = new(
        "net10.0",
        new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
        Path.Combine("ref", "net10.0"));
}
