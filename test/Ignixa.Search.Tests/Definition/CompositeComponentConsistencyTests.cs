// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Tests.Definition;

/// <summary>
/// A census of composite search parameters whose component expressions index values of another kind than the component
/// declares, so no query could match them. Each is either repaired in <c>CompositeComponentDefinitionRepairs</c> or listed
/// here, and in-memory search refuses the listed ones instead of answering "nothing".
/// </summary>
public class CompositeComponentConsistencyTests
{
    /// <summary>Shipped composites that stay inconsistent after repair, with the component at fault.</summary>
    private static readonly IReadOnlySet<(FhirVersion Version, string Url)> KnownInconsistent = new HashSet<(FhirVersion, string)>
    {
        // location.reference selects the reference string, which indexes as a string under a reference component.
        (FhirVersion.R5, "http://hl7.org/fhir/SearchParameter/Encounter-location-period"),

        // artifact is a canonical, and no converter turns a canonical into a reference value.
        (FhirVersion.R6, "http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact-conformance"),
        (FhirVersion.R6, "http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact-phase"),
    };

    public static TheoryData<FhirVersion> Versions => new(FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6);

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenShippedComposites_WhenTheirComponentsAreTyped_ThenOnlyTheKnownOnesAreInconsistent(FhirVersion version)
    {
        SearchParameterDefinitionManager definitions = Definitions(version);

        HashSet<(FhirVersion, string)> inconsistent = [.. definitions.AllSearchParameters
            .Where(parameter => parameter.Type == SearchParamType.Composite && parameter.ComponentInconsistency?.Value is not null)
            .Select(parameter => (version, parameter.Url.OriginalString))];

        inconsistent.ShouldBe([.. KnownInconsistent.Where(known => known.Version == version)], ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenDocumentReferenceRelationship_WhenLoaded_ThenItsSwappedComponentExpressionsAreRepaired(FhirVersion version)
    {
        definitionsHave(version).ShouldBe(("target", "code"));

        static (string, string) definitionsHave(FhirVersion version)
        {
            Definitions(version).TryGetSearchParameter("DocumentReference", "relationship", out SearchParameterInfo relationship).ShouldBeTrue();
            relationship.ComponentInconsistency.ShouldNotBeNull().Value.ShouldBeNull();
            return (relationship.Component[0].Expression, relationship.Component[1].Expression);
        }
    }

    [Fact]
    public void GivenACompositeWhoseReferenceComponentSelectsACode_WhenChecked_ThenItIsReportedAsInconsistent()
    {
        IFhirSchemaProvider schema = FhirVersion.R4.GetSchemaProvider();
        SearchParameterDefinitionManager definitions = Definitions(FhirVersion.R4);
        definitions.TryGetSearchParameter("DocumentReference", "relatesto", out SearchParameterInfo relatesTo).ShouldBeTrue();
        definitions.TryGetSearchParameter("DocumentReference", "relation", out SearchParameterInfo relation).ShouldBeTrue();
        var swapped = new SearchParameterInfo(
            "swapped",
            "swapped",
            SearchParamType.Composite,
            new Uri("http://example.org/SearchParameter/swapped"),
            [new SearchParameterComponentInfo(relatesTo.Url, "code") { ResolvedSearchParameter = relatesTo }, new SearchParameterComponentInfo(relation.Url, "target") { ResolvedSearchParameter = relation }],
            "DocumentReference.relatesTo",
            baseResourceTypes: ["DocumentReference"]);

        CompositeComponentConsistency.Inconsistency(swapped, new FhirPathAnalyzer(schema), Converters(schema))
            .ShouldNotBeNull().ShouldContain("component 0 ('code') is declared Reference");
    }

    [Fact]
    public void GivenAKnownInconsistentComposite_WhenSearchedInMemory_ThenTheSearchIsRejectedBeforeAnyEntryIsRead()
    {
        IFhirSchemaProvider schema = FhirVersion.R6.GetSchemaProvider();
        SearchParameterDefinitionManager definitions = Definitions(FhirVersion.R6);
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => definitions, new SearchParameterExpressionParser(new ReferenceSearchValueParser(schema, NullFhirBaseUriProvider.Instance), schema), schema),
            definitions);
        SearchOptions options = builder.Build("TestScript", new QueryParameterParser().Parse("scope-artifact-phase=http://x$unit"), schema);
        options.UnsupportedParams.ShouldBeEmpty();
        var interpreter = new SearchQueryInterpreter();

        SearchOperationNotSupportedException rejected = Should.Throw<SearchOperationNotSupportedException>(
            () => options.Expression.AcceptVisitor(interpreter, interpreter.InitialContext));

        rejected.Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull().ShouldContain("scope-artifact-phase");
    }

    private static SearchParameterDefinitionManager Definitions(FhirVersion version) =>
        new(version.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    private static HashSet<(string FhirType, Type SearchValueType)> Converters(IFhirSchemaProvider schema)
    {
        var referenceParser = new ReferenceSearchValueParser(schema, NullFhirBaseUriProvider.Instance);
        return [.. SearchIndexerFactory
            .CreateConverters(schema, referenceParser, new LightweightReferenceToElementResolver(referenceParser, schema))
            .SelectMany(converter => converter.FhirTypes.Select(fhirType => (fhirType, converter.SearchValueType)))];
    }
}
