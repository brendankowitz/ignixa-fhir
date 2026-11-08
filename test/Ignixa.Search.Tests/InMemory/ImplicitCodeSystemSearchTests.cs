// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using static Ignixa.Search.Tests.InMemory.SearchCorpus;

namespace Ignixa.Search.Tests.InMemory;

/// <summary>
/// A FHIR <c>code</c> such as <c>Patient.gender</c> carries no system in the instance; it is indexed under the system
/// its binding implies, so a <c>system|code</c> token search must name that system's canonical URL.
/// </summary>
public class ImplicitCodeSystemSearchTests
{
    private static readonly string[] Patients =
    [
        """{"resourceType":"Patient","id":"f","gender":"female"}""",
        """{"resourceType":"Patient","id":"m","gender":"male"}""",
    ];

    // Regressions: each row fails at bdb927b, which indexed gender under http://hl7.org/fhir/CodeSystem/administrative-gender.
    [Theory]
    [InlineData("gender=http://hl7.org/fhir/administrative-gender|female", "f")]
    [InlineData("gender=http://hl7.org/fhir/CodeSystem/administrative-gender|female", "")]
    [InlineData("gender:not=http://hl7.org/fhir/administrative-gender|female", "m")]
    public void GivenAnImplicitlyCodedElement_WhenSearchedBySystemAndCode_ThenOnlyTheCanonicalSystemMatches(string query, string expected)
    {
        Match("Patient", query, Patients).ShouldBe(Ids(expected));
    }

    // Guard: passes at bdb927b and was mutation-checked; a code-only search ignores the system.
    [Fact]
    public void GivenAnImplicitlyCodedElement_WhenSearchedByCodeAlone_ThenItMatches()
    {
        Match("Patient", "gender=female", Patients).ShouldBe(["f"]);
    }

    private static string[] Ids(string expected) => expected.Length == 0 ? [] : expected.Split(',');
}
