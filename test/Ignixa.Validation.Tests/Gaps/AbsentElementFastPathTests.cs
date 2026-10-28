// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Gaps;

/// <summary>
/// Checks skip an element the raw JSON does not carry without asking for its children (Task 9b fix round 1, F10). These
/// guards pin that an element carried only by its shadow (<c>_birthDate</c>) is still present to them. They pass at b0f34da.
/// </summary>
public class AbsentElementFastPathTests
{
    [Fact]
    public void GivenASingularPrimitiveRepeatedOnlyByItsShadow_WhenValidatedAtSpec_ThenItsCardinalityIsStillChecked()
    {
        var result = JsonResourceValidator.Validate("""
            {"resourceType":"Patient","id":"p","_birthDate":[{"id":"a"},{"id":"b"}]}
            """, ValidationDepth.Spec);

        result.Issues.ShouldContain(i => i.Code == "cardinality-violation" && i.Path == "Patient.birthDate");
    }
}
