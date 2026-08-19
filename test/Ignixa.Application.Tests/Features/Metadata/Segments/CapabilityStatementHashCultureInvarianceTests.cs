// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Metadata.Segments;

/// <summary>
/// Pins the CapabilityStatement version-hash segments to ordinal ordering, because the hash they
/// return is used as the cache key / conformance version for the whole CapabilityStatement.
/// </summary>
/// <remarks>
/// Both segments previously ordered <see cref="SearchParameterInfo"/> collections with the default
/// (linguistic, host-culture-sensitive) string comparer before hashing them with SHA-256. Ordinal
/// puts 'C' (U+0043) before 'c' (U+0063); linguistic collation treats case as a tie-break after the
/// base letter, so "ClinicalImpression-assessor" and "clinical-code" swap order under every
/// non-ordinal culture (measured: en-US, de-DE, fr-FR, ar-SA, th-TH all agree with each other and
/// disagree with ordinal). A server's CapabilityStatement cache key - and hence its decision to treat
/// a cached statement as stale - would therefore depend on the host locale.
///
/// Both tests carry a golden constant instead of recomputing the expected hash, so a future change to
/// the ordering or the hash payload fails loudly rather than silently re-baselining.
/// </remarks>
public class CapabilityStatementHashCultureInvarianceTests
{
    /// <summary>
    /// SHA-256 of "http://hl7.org/fhir/SearchParameter/ClinicalImpression-assessor|http://hl7.org/fhir/SearchParameter/clinical-code"
    /// (Base64), i.e. the two URLs in ordinal order. Under the previous linguistic ordering, every
    /// culture in <see cref="CorruptingCultures"/> reverses that pair and produces a different hash.
    /// </summary>
    private const string ExpectedSearchParameterHash = "iPZULBEcheZAsZtNe62Co+PQX8o0YcY1hY6gxDqP2bU=";

    /// <summary>
    /// SHA-256 of "Bref:Observation:Practitioner|aref:Encounter:Patient" (Base64), i.e. the two
    /// reference-parameter codes in ordinal order. Same case-tie-break hazard as
    /// <see cref="ExpectedSearchParameterHash"/>, on the Code field instead of the Url.
    /// </summary>
    private const string ExpectedIncludeRevIncludeHash = "wWyYtZa5g+ZeXjsv2Wgm0zlwfm0o4z66nogdmvCRpi8=";

    private static readonly string[] PatientOnly = ["Patient"];
    private static readonly string[] EncounterOnly = ["Encounter"];
    private static readonly string[] PractitionerOnly = ["Practitioner"];
    private static readonly string[] ObservationOnly = ["Observation"];

    public static TheoryData<string> CorruptingCultures =>
        new() { "de-DE", "fr-FR", "ar-SA", "th-TH", "en-US" };

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenCaseDivergentSearchParameterUrls_WhenTheHostCultureVaries_ThenTheVersionHashIsOrdinallyOrdered(string cultureName)
    {
        // Arrange
        var manager = Substitute.For<ISearchParameterDefinitionManager>();
        manager.AllSearchParameters.Returns(new[]
        {
            new SearchParameterInfo(
                "clinical-code",
                "code",
                SearchParamType.Token,
                new Uri("http://hl7.org/fhir/SearchParameter/clinical-code")),
            new SearchParameterInfo(
                "ClinicalImpression-assessor",
                "assessor",
                SearchParamType.Reference,
                new Uri("http://hl7.org/fhir/SearchParameter/ClinicalImpression-assessor")),
        });

        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetSearchParameterDefinitionManager(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(manager);

        var segment = new SearchParameterCapabilitySegment(versionContext, NullLogger<SearchParameterCapabilitySegment>.Instance);
        var context = new CapabilityContext(FhirVersion.R4);

        // Act
        string hash = UnderCulture(cultureName, () => segment.GetVersionHashAsync(context, CancellationToken.None).GetAwaiter().GetResult());

        // Assert
        hash.ShouldBe(ExpectedSearchParameterHash);
    }

    [Theory]
    [MemberData(nameof(CorruptingCultures))]
    public void GivenCaseDivergentReferenceParameterCodes_WhenTheHostCultureVaries_ThenTheVersionHashIsOrdinallyOrdered(string cultureName)
    {
        // Arrange
        var manager = Substitute.For<ISearchParameterDefinitionManager>();
        manager.AllSearchParameters.Returns(new[]
        {
            new SearchParameterInfo(
                "a-ref-param",
                "aref",
                SearchParamType.Reference,
                targetResourceTypes: PatientOnly,
                baseResourceTypes: EncounterOnly),
            new SearchParameterInfo(
                "B-ref-param",
                "Bref",
                SearchParamType.Reference,
                targetResourceTypes: PractitionerOnly,
                baseResourceTypes: ObservationOnly),
        });

        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetSearchParameterDefinitionManager(Arg.Any<FhirVersion>()).Returns(manager);

        var segment = new IncludeRevIncludeCapabilitySegment(versionContext, NullLogger<IncludeRevIncludeCapabilitySegment>.Instance);
        var context = new CapabilityContext(FhirVersion.R4);

        // Act
        string hash = UnderCulture(cultureName, () => segment.GetVersionHashAsync(context, CancellationToken.None).GetAwaiter().GetResult());

        // Assert
        hash.ShouldBe(ExpectedIncludeRevIncludeHash);
    }

    private static string UnderCulture(string cultureName, Func<string> act)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            return act();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
