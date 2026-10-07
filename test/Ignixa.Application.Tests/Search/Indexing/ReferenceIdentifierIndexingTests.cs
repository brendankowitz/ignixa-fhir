// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Indexing;

public class ReferenceIdentifierIndexingTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();
    private readonly ISearchIndexer _indexer;

    public ReferenceIdentifierIndexingTests()
    {
        var manager = new SearchParameterDefinitionManager(
            _schemaProvider,
            NullLogger<SearchParameterDefinitionManager>.Instance);

        _indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            NullLoggerFactory.Instance,
            manager,
            NullFhirBaseUriProvider.Instance);
    }

    [Fact]
    public void GivenIdentifierOnlyReference_WhenIndexing_ThenDerivedTokenEntryIsProduced()
    {
        IElement encounter = CreateEncounter("""
            {
              "identifier": {
                "system": "http://example.org/mrn",
                "value": "1234"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        SearchIndexEntry entry = entries.Single(e => e.SearchParameter.Code == "subject:identifier");
        TokenSearchValue token = entry.Value.ShouldBeOfType<TokenSearchValue>();
        token.System.ShouldBe("http://example.org/mrn");
        token.Code.ShouldBe("1234");
        entries.ShouldNotContain(e => e.SearchParameter.Code == "subject" && e.Value is ReferenceSearchValue);
    }

    [Fact]
    public void GivenReferenceWithLiteralAndIdentifier_WhenIndexing_ThenReferenceAndDerivedTokenEntriesAreProduced()
    {
        IElement encounter = CreateEncounter("""
            {
              "reference": "Patient/123",
              "identifier": {
                "system": "http://example.org/mrn",
                "value": "1234"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        entries.Any(e => e.SearchParameter.Code == "subject" && e.Value is ReferenceSearchValue).ShouldBeTrue();
        entries.Any(e =>
            e.SearchParameter.Code == "subject:identifier" &&
            e.Value is TokenSearchValue token &&
            token.System == "http://example.org/mrn" &&
            token.Code == "1234").ShouldBeTrue();
    }

    [Fact]
    public void GivenReferenceIdentifierWithOnlySystem_WhenIndexing_ThenDerivedTokenEntryIsNotProduced()
    {
        IElement encounter = CreateEncounter("""
            {
              "identifier": {
                "system": "http://example.org/mrn"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        entries.ShouldNotContain(e => e.SearchParameter.Code == "subject:identifier");
    }

    [Fact]
    public void GivenTokenComponentSelectingAReference_WhenIndexing_ThenCompositeStillIndexesTheReference()
    {
        // The shipped DocumentReference-relationship composite pairs the Token 'relation' definition with
        // the expression 'target', a Reference. The indexer resolves that by falling back to type inference
        // when no (Reference -> Token) converter exists; ':identifier' support must not make one exist.
        IElement documentReference = ResourceJsonNode.Parse("""
            {
              "resourceType": "DocumentReference",
              "status": "current",
              "content": [ { "attachment": { "contentType": "text/plain" } } ],
              "relatesTo": [
                {
                  "code": "appends",
                  "target": { "reference": "DocumentReference/other" }
                }
              ]
            }
            """).ToElement(_schemaProvider);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(documentReference);

        CompositeIndexSearchValue relationship = entries
            .Single(e => e.SearchParameter.Code == "relationship")
            .Value.ShouldBeOfType<CompositeIndexSearchValue>();
        relationship.Components[0].ShouldHaveSingleItem().ShouldBeOfType<TokenSearchValue>().Code.ShouldBe("appends");
        ReferenceSearchValue target = relationship.Components[1].ShouldHaveSingleItem().ShouldBeOfType<ReferenceSearchValue>();
        target.ResourceType.ShouldBe("DocumentReference");
        target.ResourceId.ShouldBe("other");
    }

    [Fact]
    public void GivenReferenceParameterSelectingANonReference_WhenIndexing_ThenDerivedParameterReportsNoUnsupportedElement()
    {
        // CarePlan-instantiates-canonical is Reference-typed but selects a canonical, which has no
        // identifier. There is nothing for ':identifier' to index, so the derived parameter must match
        // nothing quietly rather than warn that a converter is missing.
        var captured = new List<(LogLevel Level, string Message)>();
        ISearchIndexer indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            new CapturingLoggerFactory(captured),
            new SearchParameterDefinitionManager(_schemaProvider, NullLogger<SearchParameterDefinitionManager>.Instance),
            NullFhirBaseUriProvider.Instance);
        IElement carePlan = ResourceJsonNode.Parse("""
            {
              "resourceType": "CarePlan",
              "status": "active",
              "intent": "plan",
              "instantiatesCanonical": [ "http://example.org/PlanDefinition/p1" ],
              "subject": { "reference": "Patient/123" }
            }
            """).ToElement(_schemaProvider);

        IReadOnlyCollection<SearchIndexEntry> entries = indexer.Extract(carePlan);

        entries.ShouldNotContain(e => e.SearchParameter.Code == "instantiates-canonical:identifier");
        captured.ShouldNotContain(entry => entry.Message.Contains("#identifier", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenReferenceParameterSelectingABareCodeableReference_WhenIndexing_ThenDerivedTokenEntryIsProducedFromTheEmbeddedReference()
    {
        // No shipped R5/R6 reference search parameter selects a bare CodeableReference element: every
        // generated one that targets a CodeableReference-typed source already narrows its own expression
        // to '.reference' (e.g. CarePlan-condition is 'CarePlan.addresses.reference', confirmed by walking
        // every generated R5/R6 schema element against every generated reference parameter's expression).
        // This proves the defensive 'ofType(CodeableReference).reference' branch works for a custom/IG
        // parameter that does not narrow, the same shape CodeableReferenceToReferenceSearchValueConverter
        // exists to index for the plain (non-identifier) reference search.
        var r5Schema = new R5CoreSchemaProvider();
        var manager = new SearchParameterDefinitionManager(r5Schema, NullLogger<SearchParameterDefinitionManager>.Instance);
        manager.AddNewSearchParameters([BareCodeableReferenceParameter(r5Schema)]);
        ISearchIndexer indexer = SearchIndexerFactory.CreateInstance(
            r5Schema,
            NullLoggerFactory.Instance,
            manager,
            NullFhirBaseUriProvider.Instance);

        IElement carePlan = ResourceJsonNode.Parse("""
            {
              "resourceType": "CarePlan",
              "status": "active",
              "intent": "plan",
              "subject": { "reference": "Patient/123" },
              "addresses": [
                {
                  "reference": {
                    "identifier": {
                      "system": "http://example.org/condition-ids",
                      "value": "c-1"
                    }
                  }
                }
              ]
            }
            """).ToElement(r5Schema);

        IReadOnlyCollection<SearchIndexEntry> entries = indexer.Extract(carePlan);

        SearchIndexEntry entry = entries.Single(e => e.SearchParameter.Code == "addresses-bare:identifier");
        TokenSearchValue token = entry.Value.ShouldBeOfType<TokenSearchValue>();
        token.System.ShouldBe("http://example.org/condition-ids");
        token.Code.ShouldBe("c-1");
    }

    private static IElement BareCodeableReferenceParameter(IFhirSchemaProvider schema)
    {
        const string json = """
            {
              "resourceType": "SearchParameter",
              "id": "careplan-addresses-bare",
              "url": "http://example.org/fhir/SearchParameter/careplan-addresses-bare",
              "name": "addresses-bare",
              "status": "active",
              "code": "addresses-bare",
              "base": [ "CarePlan" ],
              "type": "reference",
              "expression": "CarePlan.addresses",
              "target": [ "Condition" ]
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(schema);
    }

    [Fact]
    public void GivenLogicalReferenceWithMatchingTypeName_WhenIndexing_ThenTypeFilteredDerivedTokenEntryIsProduced()
    {
        // 'patient' (clinical-patient) is type-filtered: its Encounter branch is
        // 'Encounter.subject.where(resolve() is Patient)'. A logical reference (no 'reference' string)
        // cannot resolve(), so only the 'type' honoring rewrite lets this match - the motivating scenario
        // for this task, e.g. GET /Encounter?patient:identifier=http://example.org/facilityA|1234.
        IElement encounter = CreateEncounter("""
            {
              "type": "Patient",
              "identifier": {
                "system": "http://example.org/facilityA",
                "value": "1234"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        SearchIndexEntry entry = entries.Single(e => e.SearchParameter.Code == "patient:identifier");
        TokenSearchValue token = entry.Value.ShouldBeOfType<TokenSearchValue>();
        token.System.ShouldBe("http://example.org/facilityA");
        token.Code.ShouldBe("1234");
    }

    [Fact]
    public void GivenLogicalReferenceWithNonMatchingTypeName_WhenIndexing_ThenNotIndexedUnderTypeFilteredParameterButIsUnderTheUnfilteredOne()
    {
        // 'type: Group' does not satisfy 'is Patient' (nor the rewrite's 'type = Patient' alternatives), so
        // 'patient:identifier' (type-filtered to Patient) must not match. 'subject:identifier' (Encounter's
        // own 'Encounter.subject', unfiltered - targets both Group and Patient) still indexes it: the
        // rewrite only tightens the type-filtered derived parameter, it does not loosen the unfiltered one.
        IElement encounter = CreateEncounter("""
            {
              "type": "Group",
              "identifier": {
                "system": "http://example.org/mrn",
                "value": "g-1"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        entries.ShouldNotContain(e => e.SearchParameter.Code == "patient:identifier");
        SearchIndexEntry entry = entries.Single(e => e.SearchParameter.Code == "subject:identifier");
        TokenSearchValue token = entry.Value.ShouldBeOfType<TokenSearchValue>();
        token.System.ShouldBe("http://example.org/mrn");
        token.Code.ShouldBe("g-1");
    }

    [Fact]
    public void GivenLogicalReferenceWithAbsoluteStructureDefinitionTypeUrl_WhenIndexing_ThenTypeFilteredDerivedTokenEntryIsProduced()
    {
        // Reference.type is a uri; the absolute canonical form must match exactly like the relative name.
        IElement encounter = CreateEncounter("""
            {
              "type": "http://hl7.org/fhir/StructureDefinition/Patient",
              "identifier": {
                "system": "http://example.org/facilityA",
                "value": "1234"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        SearchIndexEntry entry = entries.Single(e => e.SearchParameter.Code == "patient:identifier");
        TokenSearchValue token = entry.Value.ShouldBeOfType<TokenSearchValue>();
        token.System.ShouldBe("http://example.org/facilityA");
        token.Code.ShouldBe("1234");
    }

    [Fact]
    public void GivenLiteralReferenceWithIdentifier_WhenIndexing_ThenTypeFilteredDerivedTokenEntryIsStillProduced()
    {
        // Regression guard: a literal 'Patient/123' reference already resolves (resolve() is Patient was
        // already true pre-rewrite via LightweightReferenceToElementResolver), with no 'type' element at
        // all. The rewrite must not require 'type' to be present when resolve() already succeeds.
        IElement encounter = CreateEncounter("""
            {
              "reference": "Patient/123",
              "identifier": {
                "system": "http://example.org/mrn",
                "value": "1234"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        SearchIndexEntry entry = entries.Single(e => e.SearchParameter.Code == "patient:identifier");
        TokenSearchValue token = entry.Value.ShouldBeOfType<TokenSearchValue>();
        token.System.ShouldBe("http://example.org/mrn");
        token.Code.ShouldBe("1234");
    }

    [Fact]
    public void GivenLogicalReferenceWithNoTypeAtAll_WhenIndexing_ThenNotIndexedUnderTypeFilteredParameter()
    {
        // No 'type' and no 'reference': resolve() fails (nothing to look up) and none of the 'type = ...'
        // alternatives has anything to compare, so the type-filtered parameter must not match.
        IElement encounter = CreateEncounter("""
            {
              "identifier": {
                "system": "http://example.org/mrn",
                "value": "1234"
              }
            }
            """);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(encounter);

        entries.ShouldNotContain(e => e.SearchParameter.Code == "patient:identifier");
    }

    private IElement CreateEncounter(string subject)
    {
        string json = $$"""
            {
              "resourceType": "Encounter",
              "status": "planned",
              "class": {
                "system": "http://terminology.hl7.org/CodeSystem/v3-ActCode",
                "code": "AMB"
              },
              "subject": {{subject}}
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(_schemaProvider);
    }
}
