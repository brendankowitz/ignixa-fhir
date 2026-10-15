// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Tests.Indexing;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Tests.Definition;

/// <summary>
/// A census of composite search parameters whose component expressions index values of another kind than the component
/// declares, so no query could match them. Each is either repaired in <c>CompositeComponentDefinitionRepairs</c> or listed in
/// <see cref="KnownInconsistentComposites"/>, which in-memory search refuses instead of answering "nothing";
/// <see cref="CompositeComponentConsistency"/> is the oracle that keeps that table honest.
/// </summary>
public class CompositeComponentConsistencyTests
{
    /// <summary>
    /// Shipped composites the oracle cannot type, each with the reason it is not a finding. An unlisted one fails the census,
    /// so a composite the analyzer gives up on is never counted as consistent.
    /// </summary>
    private static readonly IReadOnlyDictionary<(FhirVersion Version, string Url), string> Untyped = new Dictionary<(FhirVersion, string), string>
    {
        [(FhirVersion.Stu3, "http://hl7.org/fhir/SearchParameter/Sequence-coordinate")] = PercentResource,
        [(FhirVersion.R4, "http://hl7.org/fhir/SearchParameter/MolecularSequence-chromosome-variant-coordinate")] = PercentResource,
        [(FhirVersion.R4, "http://hl7.org/fhir/SearchParameter/MolecularSequence-referenceseqid-variant-coordinate")] = PercentResource,
        [(FhirVersion.R4B, "http://hl7.org/fhir/SearchParameter/MolecularSequence-chromosome-variant-coordinate")] = PercentResource,
        [(FhirVersion.R4B, "http://hl7.org/fhir/SearchParameter/MolecularSequence-referenceseqid-variant-coordinate")] = PercentResource,
        [(FhirVersion.R4B, "http://hl7.org/fhir/SearchParameter/Group-characteristic-value")] = UnionOfCasts,
        [(FhirVersion.R5, "http://hl7.org/fhir/SearchParameter/Group-characteristic-value")] = UnionOfCasts,
        [(FhirVersion.R6, "http://hl7.org/fhir/SearchParameter/Group-characteristic-value")] = UnionOfCasts,
    };

    private const string PercentResource =
        "The component reads %resource, which the analyzer does not bind inside a composite's context; the components select "
        + "referenceSeq strings for a string component, which index as declared.";

    private const string UnionOfCasts =
        "The value component is a parenthesised union of casts the analyzer types as nothing; it selects CodeableConcept or "
        + "boolean, both of which index as the declared token (the reviewer's Group fixture matches it).";

    public static TheoryData<FhirVersion> Versions => new(FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6);

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenShippedComposites_WhenTypedByTheOracle_ThenTheInconsistentOnesAreExactlyTheStaticTable(FhirVersion version)
    {
        IFhirSchemaProvider schema = version.GetSchemaProvider();
        var analyzer = new FhirPathAnalyzer(schema);
        HashSet<(string FhirType, Type SearchValueType)> converters = Converters(schema);
        List<(string Url, CompositeConsistencyVerdict Verdict, string? Detail)> verdicts = [.. Definitions(version).AllSearchParameters
            .Where(parameter => parameter.Type == SearchParamType.Composite)
            .DistinctBy(parameter => parameter.Url)
            .Select(parameter => CompositeComponentConsistency.Check(parameter, analyzer, converters) is var (verdict, detail)
                ? (parameter.Url.OriginalString, verdict, detail)
                : default)];

        verdicts.Where(row => row.Verdict == CompositeConsistencyVerdict.Unknown && !Untyped.ContainsKey((version, row.Url)))
            .Select(row => $"{row.Url}: {row.Detail}")
            .ShouldBeEmpty("composites the oracle cannot type must be listed in Untyped with a reason");
        verdicts.Where(row => row.Verdict == CompositeConsistencyVerdict.Unknown).Select(row => row.Url)
            .ShouldBe([.. Untyped.Keys.Where(key => key.Version == version).Select(key => key.Url)], ignoreOrder: true, customMessage: "an Untyped entry the analyzer now types must be removed");
        HashSet<(FhirVersion, string, int)> documentedUnresolved = [.. KnownCompositeComponentDivergences.All.Select(divergence => divergence.Key)];
        verdicts.Where(row => row.Verdict == CompositeConsistencyVerdict.Unresolved)
            .SelectMany(row => row.Detail!.Split(',').Select(index => (row.Url, Index: int.Parse(index, System.Globalization.CultureInfo.InvariantCulture))))
            .Where(row => !documentedUnresolved.Contains((version, row.Url, row.Index)))
            .Select(row => $"{row.Url} component {row.Index}")
            .ShouldBeEmpty("an unresolved component must be recorded in KnownCompositeComponentDivergences");
        verdicts.Where(row => row.Verdict == CompositeConsistencyVerdict.Inconsistent).Select(row => row.Url)
            .ShouldBe(
                [.. KnownInconsistentComposites.All.Keys.Where(key => key.Version == version).Select(key => key.Url)],
                ignoreOrder: true,
                customMessage: "the oracle's findings must equal KnownInconsistentComposites; update the table with the regenerated definitions");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenShippedComposites_WhenLoaded_ThenExactlyTheTablesEntriesAreMarkedInconsistent(FhirVersion version)
    {
        Definitions(version).AllSearchParameters
            .Where(parameter => parameter.Type == SearchParamType.Composite && parameter.ComponentInconsistency is not null)
            .Select(parameter => parameter.Url.OriginalString)
            .Distinct()
            .ShouldBe([.. KnownInconsistentComposites.All.Keys.Where(key => key.Version == version).Select(key => key.Url)], ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenDocumentReferenceRelationship_WhenLoaded_ThenItsSwappedComponentExpressionsAreRepaired(FhirVersion version)
    {
        Definitions(version).TryGetSearchParameter("DocumentReference", "relationship", out SearchParameterInfo relationship).ShouldBeTrue();

        (relationship.Component[0].Expression, relationship.Component[1].Expression).ShouldBe(("target", "code"));
        relationship.ComponentInconsistency.ShouldBeNull();
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

        (CompositeConsistencyVerdict verdict, string? detail) = CompositeComponentConsistency.Check(swapped, new FhirPathAnalyzer(schema), Converters(schema));

        verdict.ShouldBe(CompositeConsistencyVerdict.Inconsistent);
        detail.ShouldNotBeNull().ShouldContain("component 0 ('code') is declared Reference");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GivenAnUnresolvedOrUntypedComponentBeforeAnInconsistentOne_WhenChecked_ThenTheInconsistencyIsStillReported(bool unresolved)
    {
        // Regression: the oracle returned at the first component that was not consistent, so an earlier unresolved or
        // untyped component - verdicts the census excuses - hid a later inconsistent one.
        IFhirSchemaProvider schema = FhirVersion.R4.GetSchemaProvider();
        SearchParameterDefinitionManager definitions = Definitions(FhirVersion.R4);
        definitions.TryGetSearchParameter("DocumentReference", "relatesto", out SearchParameterInfo relatesTo).ShouldBeTrue();
        SearchParameterComponentInfo first = unresolved
            ? new SearchParameterComponentInfo(new Uri("http://example.org/SearchParameter/missing"), "target")
            : new SearchParameterComponentInfo(relatesTo.Url, "%resource.nothing") { ResolvedSearchParameter = relatesTo };
        var composite = new SearchParameterInfo(
            "masked",
            "masked",
            SearchParamType.Composite,
            new Uri("http://example.org/SearchParameter/masked"),
            [first, new SearchParameterComponentInfo(relatesTo.Url, "code") { ResolvedSearchParameter = relatesTo }],
            "DocumentReference.relatesTo",
            baseResourceTypes: ["DocumentReference"]);

        (CompositeConsistencyVerdict verdict, string? detail) = CompositeComponentConsistency.Check(composite, new FhirPathAnalyzer(schema), Converters(schema));

        verdict.ShouldBe(CompositeConsistencyVerdict.Inconsistent);
        detail.ShouldNotBeNull().ShouldContain("component 1 ('code') is declared Reference");
    }

    [Fact]
    public void GivenEveryShippedTableEntry_WhenSearched_ThenItIsRefusedBeforeInMemorySearchReadsTheTable()
    {
        // Guard on the documented reach of KnownInconsistentComposites: none of its entries reaches the interpreter's
        // check, so the table is a backstop. A new entry needs a row here saying where it is refused.
        Dictionary<(FhirVersion, string), (string ResourceType, string Query, bool RefusedByBinder)> refusals = new()
        {
            [(FhirVersion.R5, "http://hl7.org/fhir/SearchParameter/Composition-section-code-text")] = ("Composition", "section-code-text=10154-3$chief", true),
            [(FhirVersion.R6, "http://hl7.org/fhir/SearchParameter/Composition-section-code-text")] = ("Composition", "section-code-text=10154-3$chief", true),
            [(FhirVersion.R5, "http://hl7.org/fhir/SearchParameter/Encounter-location-period")] = ("Encounter", "location-period=Location/l1$ge2020", false),
        };

        refusals.Keys.ShouldBe(KnownInconsistentComposites.All.Keys, ignoreOrder: true);
        foreach (((FhirVersion version, string url), (string resourceType, string query, bool refusedByBinder)) in refusals)
        {
            VersionedCorpus corpus = VersionedCorpus.For(version);
            if (refusedByBinder)
            {
                Should.Throw<SearchOperationNotSupportedException>(() => corpus.Options(resourceType, query), $"{version} {url}");
            }
            else
            {
                corpus.Options(resourceType, query).UnsupportedParams.ShouldNotBeEmpty($"{version} {url}");
            }
        }
    }

    [Fact]
    public void GivenAKnownInconsistentComposite_WhenSearchedInMemory_ThenTheSearchIsRejectedBeforeAnyEntryIsRead()
    {
        // Guard. Every shipped entry is refused before the interpreter (the binder or the options builder), so the
        // expression is built by hand to reach the interpreter's backstop.
        Definitions(FhirVersion.R5).TryGetSearchParameter("Encounter", "location-period", out SearchParameterInfo locationPeriod).ShouldBeTrue();
        Expression expression = Expression.SearchParameter(locationPeriod, Expression.StringEquals(FieldName.ReferenceResourceId, 0, "l1", false));
        var interpreter = new SearchQueryInterpreter();

        SearchOperationNotSupportedException rejected = Should.Throw<SearchOperationNotSupportedException>(
            () => expression.AcceptVisitor(interpreter, interpreter.InitialContext));

        rejected.Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull().ShouldContain("location-period");
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
