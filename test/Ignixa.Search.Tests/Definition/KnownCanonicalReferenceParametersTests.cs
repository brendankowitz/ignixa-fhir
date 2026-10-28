// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Visitors;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Tests.Definition;

/// <summary>
/// A census of shipped reference parameters whose expressions select a <c>canonical</c>, which keeps
/// <see cref="KnownCanonicalReferenceParameters"/> (what in-memory search indexes, follows and refuses) equal to what the
/// FHIRPath analyzer finds.
/// </summary>
public class KnownCanonicalReferenceParametersTests
{
    /// <summary>
    /// Shipped reference parameters the analyzer cannot type, each with the reason it selects no canonical. An unlisted one
    /// fails the census, so a parameter the analyzer gives up on is never counted as reference-only by default.
    /// </summary>
    private static readonly IReadOnlyDictionary<(FhirVersion Version, string Url), string> Untyped = new Dictionary<(FhirVersion, string), string>
    {
        [(FhirVersion.R6, "http://hl7.org/fhir/SearchParameter/Specimen-container-location")] = ThroughResolve,
        [(FhirVersion.R6, "http://hl7.org/fhir/SearchParameter/Specimen-organization")] = ThroughResolve,
    };

    private const string ThroughResolve =
        "The expression reads through resolve(), which the analyzer does not type; it selects Device.location or Device.owner, "
        + "both Reference(Location|Organization), never a canonical.";

    public static TheoryData<FhirVersion> Versions => new(FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6);

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenShippedReferenceParameters_WhenTypedByTheAnalyzer_ThenTheCanonicalOnesAreExactlyTheStaticTable(FhirVersion version)
    {
        IFhirSchemaProvider schema = version.GetSchemaProvider();
        var analyzer = new FhirPathAnalyzer(schema);
        List<(string Url, CanonicalReferenceKind Kind, bool Untyped)> typed = [.. Definitions(version).AllSearchParameters
            .Where(parameter => parameter.Type == SearchParamType.Reference && !string.IsNullOrWhiteSpace(parameter.Expression))
            .DistinctBy(parameter => parameter.Url)
            .Select(parameter => Classify(parameter, analyzer))];

        typed.Where(row => row.Untyped && !Untyped.ContainsKey((version, row.Url))).Select(row => row.Url)
            .ShouldBeEmpty("reference parameters the analyzer cannot type must be listed in Untyped with a reason");
        typed.Where(row => row.Untyped).Select(row => row.Url)
            .ShouldBe([.. Untyped.Keys.Where(key => key.Version == version).Select(key => key.Url)], ignoreOrder: true, customMessage: "an Untyped entry the analyzer now types must be removed");
        typed.Where(row => row.Kind != CanonicalReferenceKind.None).Select(row => $"{row.Url} {row.Kind}")
            .ShouldBe(
                [.. KnownCanonicalReferenceParameters.All.Where(row => row.Key.Version == version).Select(row => $"{row.Key.Url} {row.Value}")],
                ignoreOrder: true,
                customMessage: "KnownCanonicalReferenceParameters must list exactly the reference parameters typed as canonical, with their kind");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenLoadedDefinitions_WhenInspected_ThenEveryListedParameterIsMarkedWithItsKind(FhirVersion version)
    {
        Definitions(version).AllSearchParameters
            .Where(parameter => parameter.CanonicalReferences != CanonicalReferenceKind.None)
            .Select(parameter => $"{parameter.Url.OriginalString} {parameter.CanonicalReferences}")
            .Distinct()
            .ShouldBe([.. KnownCanonicalReferenceParameters.All.Where(row => row.Key.Version == version).Select(row => $"{row.Key.Url} {row.Value}")], ignoreOrder: true);
    }

    [Theory]
    [InlineData("Resource", "_profile", "Only")]
    [InlineData("QuestionnaireResponse", "questionnaire", "Only")]
    [InlineData("RequestOrchestration", "participant", "Mixed")]
    [InlineData("RequestOrchestration", "subject", "None")]
    public void GivenR5Definitions_WhenLoaded_ThenTheReviewedParametersHaveTheirKind(string resourceType, string code, string expected)
    {
        Definitions(FhirVersion.R5).TryGetSearchParameter(resourceType, code, out SearchParameterInfo parameter).ShouldBeTrue();

        parameter.CanonicalReferences.ToString().ShouldBe(expected);
    }

    private static (string Url, CanonicalReferenceKind Kind, bool Untyped) Classify(SearchParameterInfo parameter, FhirPathAnalyzer analyzer)
    {
        List<FhirPathTypeSet> inferred = [.. parameter.BaseResourceTypes.Select(type => analyzer.InferTypes(parameter.Expression, type))];
        bool canonical = inferred.Any(types => types.Types.Any(type => type.TypeName == "canonical"));
        bool other = inferred.Any(types => types.Types.Any(type => type.TypeName != "canonical"));
        CanonicalReferenceKind kind = (canonical, other) switch
        {
            (true, true) => CanonicalReferenceKind.Mixed,
            (true, false) => CanonicalReferenceKind.Only,
            _ => CanonicalReferenceKind.None,
        };
        return (parameter.Url.OriginalString, kind, !canonical && inferred.Any(types => types.HasUnknown));
    }

    private static SearchParameterDefinitionManager Definitions(FhirVersion version) =>
        new(version.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
}
