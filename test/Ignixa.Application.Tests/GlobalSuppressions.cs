// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// Test project suppressions for nullable warnings - tests use Assert which handles nulls
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "Not required in test code", Scope = "module")]
[assembly: SuppressMessage("Globalization", "CA1307:The behavior of 'string.Contains(string)' could vary based on the current user's locale settings", Justification = "Test assertions don't need culture-specific comparison", Scope = "module")]
[assembly: SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Test methods don't require null validation", Scope = "module")]

// Nullable warnings - test code purposefully works with nullable values
#pragma warning disable CS8604 // Possible null reference argument
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
