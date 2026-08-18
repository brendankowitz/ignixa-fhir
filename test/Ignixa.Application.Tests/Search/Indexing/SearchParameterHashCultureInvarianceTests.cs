// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Pins <c>CalculateSearchParameterHash</c> to ordinal ordering so the hash depends only on the search
/// parameters, never on the host locale.
/// </summary>
/// <remarks>
/// The hash is persisted and compared to decide whether a resource type needs reindexing. Ordering it with
/// the default (linguistic, culture-sensitive) string comparer therefore made a server's reindex decision a
/// function of where it runs. Measured over the real R4 parameter set across all 890 installed cultures:
/// 51 cultures order the parameter URLs differently from the invariant culture, giving 128 of 135 resource
/// types a locale-dependent hash. Two servers in one cluster with different <c>LANG</c> settings would each
/// see the other's hash as stale and reindex in a loop.
///
/// <see cref="GivenAHashInput_WhenTheHostCultureVaries_ThenTheHashIsOrdinallyOrdered"/> carries a golden
/// constant rather than recomputing the expected payload, because the value is persisted: any change to the
/// ordering OR to the payload layout must fail loudly rather than silently re-baseline.
/// </remarks>
public class SearchParameterHashCultureInvarianceTests
{
    /// <summary>
    /// The hash of <see cref="CollationSensitiveParameters"/> under ordinal ordering. Measured identical
    /// across all 891 cultures. Under the previous linguistic ordering 890 of those cultures produced
    /// 9773999221B9E61220430B4338C0D54725EB2DCCF7A69454191301C108173A04 instead - only en-US-POSIX, whose
    /// collation is already ordinal, agreed with this value.
    /// </summary>
    private const string ExpectedOrdinalHash = "EDDAEDA8C5D77C30680A7A2AFB40F7BF2B230B9348BDA4D80CC5ADD223BE1129";

    /// <summary>
    /// Cultures measured to order the real R4 search parameter URLs differently from the invariant culture,
    /// plus the usual numeric-format offenders. cs-CZ and sk-SK treat "ch" as a single collation element
    /// sorting after "h"; lt-LT and lv-LV reorder "y"; tr-TR has the dotted/dotless i; en-US-POSIX collates
    /// ordinally and so was the one culture that already agreed with the fixed behaviour.
    /// </summary>
    public static TheoryData<string> CollationDivergentCultures =>
        new() { "cs-CZ", "sk-SK", "lt-LT", "lv-LV", "tr-TR", "az-Latn-AZ", "th-TH", "cy-GB", "br-FR", "en-US-POSIX", "de-DE", "fr-FR", "ar-SA", "en-US" };

    [Theory]
    [MemberData(nameof(CollationDivergentCultures))]
    public void GivenAHashInput_WhenTheHostCultureVaries_ThenTheHashIsOrdinallyOrdered(string cultureName)
    {
        // Arrange
        List<SearchParameterInfo> parameters = CollationSensitiveParameters();

        // Act
        string hash = UnderCulture(cultureName, () => parameters.CalculateSearchParameterHash());

        // Assert
        hash.ShouldBe(ExpectedOrdinalHash);
    }

    [Theory]
    [MemberData(nameof(CollationDivergentCultures))]
    public void GivenAHashInputInReverseOrder_WhenTheHostCultureVaries_ThenTheHashIsUnchanged(string cultureName)
    {
        // Arrange - the documented contract is that input order must not affect the hash.
        List<SearchParameterInfo> reversed = CollationSensitiveParameters();
        reversed.Reverse();

        // Act
        string hash = UnderCulture(cultureName, () => reversed.CalculateSearchParameterHash());

        // Assert
        hash.ShouldBe(ExpectedOrdinalHash);
    }

    [Fact]
    public void GivenTheSameHashInput_WhenComputedUnderEveryInstalledCulture_ThenEveryCultureAgrees()
    {
        // Arrange - the theories above sample known-divergent cultures; this sweeps the whole set so a
        // culture nobody thought of cannot reintroduce the divergence.
        List<SearchParameterInfo> parameters = CollationSensitiveParameters();
        var hashesByCulture = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // Act
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.AllCultures).Append(CultureInfo.InvariantCulture))
        {
            string hash = UnderCulture(culture, () => parameters.CalculateSearchParameterHash());
            string label = culture.Name.Length == 0 ? "(invariant)" : culture.Name;

            if (!hashesByCulture.TryGetValue(hash, out List<string>? cultures)) hashesByCulture[hash] = cultures = [];

            cultures.Add(label);
        }

        // Assert
        hashesByCulture.Keys.ShouldHaveSingleItem();
        hashesByCulture.Keys.Single().ShouldBe(ExpectedOrdinalHash);
    }

    /// <summary>
    /// Two real R4 parameter URLs whose ordinal order is the reverse of their linguistic order: ordinal puts
    /// 'C' (U+0043) before 'c' (U+0063), while linguistic collation compares case only as a tie-breaker and
    /// so reaches "clinical-code" first. Measured to differ in 890 of 891 cultures, which makes this pair a
    /// reliable regression guard rather than a single locale's quirk.
    /// </summary>
    private static List<SearchParameterInfo> CollationSensitiveParameters() =>
    [
        new(
            "clinical-code",
            "code",
            SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/clinical-code"),
            expression: "Condition.code",
            targetResourceTypes: ["Patient", "Group"],
            baseResourceTypes: ["Condition", "AllergyIntolerance"]),
        new(
            "ClinicalImpression-assessor",
            "assessor",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/ClinicalImpression-assessor"),
            expression: "ClinicalImpression.assessor",
            targetResourceTypes: ["Practitioner", "PractitionerRole"],
            baseResourceTypes: ["ClinicalImpression"])
    ];

    private static string UnderCulture(string cultureName, Func<string> act) =>
        UnderCulture(new CultureInfo(cultureName), act);

    private static string UnderCulture(CultureInfo culture, Func<string> act)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            return act();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
