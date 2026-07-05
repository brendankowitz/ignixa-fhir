// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;

namespace Ignixa.FhirFakes.Tests.Scenarios.Predefined;

/// <summary>
/// Test-only scenario. Lives outside <c>Ignixa.FhirFakes</c>'s own assembly to prove
/// <see cref="ScenarioCatalog.RegisterAssembly"/> discovers scenarios from a registered external
/// assembly, matched by the <c>.Scenarios.Predefined</c> namespace suffix rather than assembly identity.
/// </summary>
public static class RegisteredTestScenario
{
    public static ScenarioContext GetRegisteredTestScenario(IFhirSchemaProvider schemaProvider)
    {
        var context = new ScenarioContext();
        context.SetAttribute("registered", true);
        return context;
    }
}
