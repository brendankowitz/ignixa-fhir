// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

// CA2234: Pass System.Uri objects instead of strings
// Rationale: In E2E tests, string URIs are more readable and maintainable
[assembly: SuppressMessage("Usage", "CA2234:Pass system uri objects instead of strings", Justification = "String URIs are acceptable in E2E tests for readability", Scope = "namespaceanddescendants", Target = "~N:Ignixa.Api.E2ETests")]

// CA2000: Dispose objects before losing scope
// Rationale: HttpClient.PostAsync/PutAsync takes ownership of HttpContent and disposes it
[assembly: SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "HttpClient owns and disposes HttpContent", Scope = "namespaceanddescendants", Target = "~N:Ignixa.Api.E2ETests")]

// xUnit1000: Test classes must be public
// Rationale: Test class is internal because it uses internal IgnixaApiFixture which accesses internal Program class via InternalsVisibleTo
[assembly: SuppressMessage("xUnit1000", "xUnit1000:Test classes must be public", Justification = "Internal test class is valid when accessing internal types via InternalsVisibleTo", Scope = "namespaceanddescendants", Target = "~N:Ignixa.Api.E2ETests")]
