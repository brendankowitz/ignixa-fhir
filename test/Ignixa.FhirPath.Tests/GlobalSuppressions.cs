/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * Global code analysis suppressions for test project.
 */

using System.Diagnostics.CodeAnalysis;

// CA1707: Remove underscores from identifiers
// Suppressed for test projects as BDD-style naming (Given_When_Then) is the preferred convention
[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "BDD-style test naming convention", Scope = "namespaceanddescendants", Target = "~N:Sparky.FhirPath.Tests")]
