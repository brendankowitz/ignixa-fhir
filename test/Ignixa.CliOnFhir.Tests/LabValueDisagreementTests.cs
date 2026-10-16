// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Patients;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.PatientFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// V15: when one analyte is coded several ways and two of the codes have readings at the very same instant with different
/// numbers in the same unit, the patient map's quirk line for it says so. New at V15: at 9f59d830 (V14) the line ended with
/// "search all", so the tests that expect the disagreement fail there.
/// </summary>
public class LabValueDisagreementTests
{
    private const string Blood = "Creatinine [Mass/volume] in Blood";
    private const string Serum = "Creatinine [Mass/volume] in Serum or Plasma";
    private const string Disagree = "; values disagree at the same time (";
    private const string Advice = "); check which code the question means";

    private static string Creatinine(string id, string code, string display, string date, string value, string unit = "mg/dL") =>
        $$$"""{"resourceType":"Observation","id":"{{{id}}}","status":"final","category":[{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/observation-category","code":"laboratory"}]}],"code":{"coding":[{"system":"http://loinc.org","code":"{{{code}}}","display":"{{{display}}}"}]},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"{{{date}}}","valueQuantity":{"value":{{{value}}},"unit":"{{{unit}}}"}}""";

    private static IReadOnlyList<string> Quirks(params string[] observations)
    {
        using TestFolder folder = Ndjson().Write("labs.ndjson", string.Join('\n', observations));
        return PatientMap.Get(Open(folder), "p1").Facts.ShouldNotBeNull().Quirks ?? [];
    }

    [Fact]
    public void GivenTwoCodesForOneAnalyteWithDifferentValuesAtTheSameInstant_WhenMapped_ThenTheQuirkNamesTheInstantAndBothValues()
    {
        IReadOnlyList<string> quirks = Quirks(
            Creatinine("k1", "38483-4", Blood, "2026-08-16T10:00:00Z", "6.07"),
            Creatinine("k2", "2160-0", Serum, "2026-08-16T10:00:00Z", "1.99"),
            Creatinine("k3", "2160-0", Serum, "2026-01-01T10:00:00Z", "1.1"));

        quirks.ShouldContain("Creatinine: loinc|2160-0 n=2, loinc|38483-4 n=1; search all; values disagree at the same time (2026-08-16: 1.99 vs 6.07 mg/dL); check which code the question means");
    }

    [Fact]
    public void GivenTheSameValuesAtTheSameInstant_WhenMapped_ThenTheQuirkOnlyNamesTheCodes()
    {
        IReadOnlyList<string> quirks = Quirks(
            Creatinine("k1", "38483-4", Blood, "2026-08-16T10:00:00Z", "1.99"),
            Creatinine("k2", "2160-0", Serum, "2026-08-16T10:00:00Z", "1.990"));

        quirks.ShouldContain(quirk => quirk.StartsWith("Creatinine: ", StringComparison.Ordinal) && quirk.EndsWith("; search all", StringComparison.Ordinal));
        quirks.ShouldNotContain(quirk => quirk.Contains(Disagree, StringComparison.Ordinal));
    }

    [Fact]
    public void GivenDifferentValuesInDifferentUnitsAtTheSameInstant_WhenMapped_ThenNothingIsConvertedAndThereIsNoDisagreement()
    {
        IReadOnlyList<string> quirks = Quirks(
            Creatinine("k1", "38483-4", Blood, "2026-08-16T10:00:00Z", "6.07", unit: "mg/dL"),
            Creatinine("k2", "2160-0", Serum, "2026-08-16T10:00:00Z", "536", unit: "umol/L"));

        quirks.ShouldNotContain(quirk => quirk.Contains(Disagree, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2026-08-15T10:00:00Z")]
    [InlineData("2026-08-16T11:00:00Z")]
    public void GivenDifferentValuesAtDifferentInstants_WhenMapped_ThenThereIsNoDisagreement(string otherInstant)
    {
        IReadOnlyList<string> quirks = Quirks(
            Creatinine("k1", "38483-4", Blood, "2026-08-16T10:00:00Z", "6.07"),
            Creatinine("k2", "2160-0", Serum, otherInstant, "1.99"));

        quirks.ShouldNotContain(quirk => quirk.Contains(Disagree, StringComparison.Ordinal));
    }

    [Fact]
    public void GivenOneCodeWithTwoValuesAtTheSameInstant_WhenMapped_ThenThereIsNoQuirkAtAll()
    {
        IReadOnlyList<string> quirks = Quirks(
            Creatinine("k1", "2160-0", Serum, "2026-08-16T10:00:00Z", "6.07"),
            Creatinine("k2", "2160-0", Serum, "2026-08-16T10:00:00Z", "1.99"));

        quirks.ShouldNotContain(quirk => quirk.StartsWith("Creatinine", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenMoreAnalytesUnderSeveralCodesThanTheQuirksListed_WhenOneDisagrees_ThenItIsListed()
    {
        string Analyte(string name, string firstCode, string secondCode, string value, string otherValue, int copies) => string.Join('\n', Enumerable.Range(0, copies).Select(copy =>
            Creatinine($"{name}-{firstCode}-{copy}", firstCode, $"{name} [Mass/volume] in Blood", $"2025-01-{copy + 1:D2}T00:00:00Z", "1") + "\n" +
            Creatinine($"{name}-{secondCode}-{copy}", secondCode, $"{name} [Mass/volume] in Serum", $"2025-02-{copy + 1:D2}T00:00:00Z", "1")))
            + "\n" + Creatinine($"{name}-x", firstCode, $"{name} [Mass/volume] in Blood", "2026-08-16T10:00:00Z", value)
            + "\n" + Creatinine($"{name}-y", secondCode, $"{name} [Mass/volume] in Serum", "2026-08-16T10:00:00Z", otherValue);

        IReadOnlyList<string> quirks = Quirks(
            Analyte("Alpha", "1000-1", "1000-2", "1", "1", copies: 5),
            Analyte("Beta", "2000-1", "2000-2", "1", "1", copies: 4),
            Analyte("Gamma", "3000-1", "3000-2", "1", "1", copies: 3),
            Analyte("Delta", "4000-1", "4000-2", "5", "6", copies: 1));

        quirks.Count(quirk => quirk.Contains("; search all", StringComparison.Ordinal)).ShouldBe(PatientMap.TopQuirks);
        quirks.ShouldContain(quirk => quirk.StartsWith("Delta:", StringComparison.Ordinal) && quirk.Contains(Disagree + "2026-08-16: 5 vs 6 mg/dL" + Advice, StringComparison.Ordinal));
    }
}
