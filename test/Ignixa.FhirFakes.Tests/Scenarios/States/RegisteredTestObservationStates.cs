// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;

namespace Ignixa.FhirFakes.Tests.Scenarios.States;

/// <summary>
/// Test-only observation state factory. Lives outside <c>Ignixa.FhirFakes</c>'s own assembly to prove
/// <see cref="ObservationStateCatalog.RegisterAssembly"/> discovers factories from a registered
/// external assembly, matched by method shape rather than assembly identity.
/// </summary>
public static class RegisteredTestObservationStates
{
    public static ObservationState RegisteredTestObservation(decimal? value = null) => new()
    {
        Code = new FhirCode("http://example.org/test", "test-code", "Test Observation"),
        Value = value,
    };
}
